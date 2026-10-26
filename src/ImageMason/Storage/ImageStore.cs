using ImageMason.Configuration;
using ImageMason.Imaging;

namespace ImageMason.Storage;

/// <summary>Images stored by output=json and upload, served from GET /images/{name} until they expire.</summary>
public sealed class ImageStore
{
    private readonly string _directory;
    private readonly TimeSpan _ttl;
    private readonly long _maxBytes;
    private readonly ILogger<ImageStore> _logger;
    private readonly object _gate = new();

    // Bytes in the directory, so a save under the cap doesn't stat every file. Counted on the first save that needs
    // it, kept up to date by saves and deletes, and counted again after cleanup or an expired read (under _gate).
    private long? _usedBytes;

    public ImageStore(ImageMasonOptions options, ILogger<ImageStore> logger)
    {
        _directory = options.StorePath;
        _ttl = TimeSpan.FromMinutes(options.ImageTtlMinutes);
        _maxBytes = options.MaxStoreBytes;
        _logger = logger;
    }

    public async Task<SavedImage> SaveAsync(byte[] data, ImageFormat format, string? filename, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_directory);

        var name = string.IsNullOrEmpty(filename)
            ? Guid.NewGuid().ToString("N") + format.Extension()
            : filename;
        if (!IsStoredName(name) || !name.EndsWith(format.Extension(), StringComparison.Ordinal))
            throw new ArgumentException("outputFilename is not a stored image name.");

        var finalPath = Path.Combine(_directory, name);
        // Unique partial so two saves of the same filename do not share a temp file.
        var partialPath = finalPath + "." + Guid.NewGuid().ToString("N") + ".partial";

        try
        {
            await File.WriteAllBytesAsync(partialPath, data, cancellationToken);

            lock (_gate)
            {
                var replaced = File.Exists(finalPath) ? new FileInfo(finalPath).Length : 0;
                MakeRoom(data.LongLength - replaced);
                File.Move(partialPath, finalPath, overwrite: true);
                _usedBytes += data.LongLength - replaced;
            }
        }
        finally
        {
            // Left only when the write or move failed.
            TryDelete(partialPath);
        }

        var expiresAt = File.GetLastWriteTimeUtc(finalPath) + _ttl;
        return new SavedImage(name, expiresAt);
    }

    public bool TryGet(string name, out StoredFile file)
    {
        file = default;
        if (!IsStoredName(name))
            return false;

        var path = Path.Combine(_directory, name);
        if (!File.Exists(path))
            return false;

        var written = File.GetLastWriteTimeUtc(path);
        var remaining = written + _ttl - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            lock (_gate)
            {
                TryDelete(path);
                _usedBytes = null;
            }
            return false;
        }

        var contentType = ImageFormatDetector.FromExtension(Path.GetExtension(name.AsSpan())).ContentType();
        file = new StoredFile(path, contentType, (int)remaining.TotalSeconds);
        return true;
    }

    /// <summary>Deletes a stored image before it expires. False when there is no such image (or it already expired).</summary>
    public bool Delete(string name)
    {
        if (!IsStoredName(name))
            return false;

        var path = Path.Combine(_directory, name);
        lock (_gate)
        {
            if (!File.Exists(path))
                return false;

            var file = new FileInfo(path);
            var expired = file.LastWriteTimeUtc + _ttl <= DateTime.UtcNow;
            var length = file.Length;
            if (!TryDelete(path))
                return false;

            _usedBytes -= length;
            return !expired;
        }
    }

    public int DeleteExpired()
    {
        if (!Directory.Exists(_directory))
            return 0;

        var cutoff = DateTime.UtcNow - _ttl;
        var deleted = 0;
        foreach (var path in Directory.EnumerateFiles(_directory))
        {
            var name = Path.GetFileName(path);
            if (!IsStoredName(name) && !IsPartialName(name))
                continue;
            if (File.GetLastWriteTimeUtc(path) >= cutoff)
                continue;
            if (TryDelete(path))
                deleted++;
        }

        if (deleted > 0)
        {
            lock (_gate)
                _usedBytes = null;
        }

        return deleted;
    }

    /// <summary>
    /// Deletes the oldest stored files until <paramref name="incomingBytes"/> fits under the cap.
    /// The new file is still written when it alone is larger than the cap.
    /// </summary>
    private void MakeRoom(long incomingBytes)
    {
        if (_maxBytes <= 0)
            return;

        _usedBytes ??= Measure().Sum(static file => file.Length);
        if (_usedBytes + incomingBytes <= _maxBytes)
            return;

        // Over the cap: count again, since files may have changed outside the service, then delete the oldest.
        var files = Measure();
        var used = files.Sum(static file => file.Length);
        files.Sort(static (a, b) => a.WrittenUtc.CompareTo(b.WrittenUtc));

        var deleted = 0;
        long freed = 0;
        foreach (var file in files)
        {
            if (used + incomingBytes <= _maxBytes)
                break;
            if (!file.Stored || !TryDelete(file.Path))
                continue;

            used -= file.Length;
            freed += file.Length;
            deleted++;
        }

        _usedBytes = used;

        if (deleted > 0)
            _logger.LogInformation("Deleted {Count} oldest stored image(s) ({Freed} bytes) to stay under {Cap} bytes",
                deleted, freed, _maxBytes);

        if (used + incomingBytes > _maxBytes)
            _logger.LogWarning("Store is {Used} bytes with a {Incoming} byte file incoming; cap is {Cap} bytes",
                used, incomingBytes, _maxBytes);
    }

    /// <summary>
    /// Every file in the directory except partial ones, which are counted once they're moved into place. Only stored
    /// images may be deleted to make room; anything else just counts.
    /// </summary>
    private List<StoredEntry> Measure()
    {
        var files = new List<StoredEntry>();
        if (!Directory.Exists(_directory))
            return files;

        foreach (var path in Directory.EnumerateFiles(_directory))
        {
            var name = Path.GetFileName(path);
            if (IsPartialName(name))
                continue;

            try
            {
                var info = new FileInfo(path);
                files.Add(new StoredEntry(path, info.Length, info.LastWriteTimeUtc, IsStoredName(name)));
            }
            catch (IOException)
            {
                // Deleted while listing.
            }
        }

        return files;
    }

    private static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    // One path segment: letters, digits, '_' or '-', then .jpg, .png, .webp, .avif or .jxl. Nothing else is served or deleted.
    public static bool IsStoredName(string name)
    {
        if (name.Length is < 5 or > 128)
            return false;

        var dot = name.IndexOf('.');
        if (dot <= 0 || dot != name.LastIndexOf('.'))
            return false;

        if (ImageFormatDetector.FromExtension(name.AsSpan(dot)) == ImageFormat.Unknown)
            return false;

        for (var i = 0; i < dot; i++)
        {
            var c = name[i];
            if (c is not (>= '0' and <= '9' or >= 'a' and <= 'z' or >= 'A' and <= 'Z' or '_' or '-'))
                return false;
        }

        return true;
    }

    private static bool IsPartialName(string name)
    {
        const string suffix = ".partial";
        if (!name.EndsWith(suffix, StringComparison.Ordinal))
            return false;

        // {storedName}.{32 hex}.partial, the temp file written before the final move.
        var stem = name[..^suffix.Length];
        var dot = stem.LastIndexOf('.');
        if (dot <= 0 || stem.Length - dot != 33)
            return false;

        for (var i = dot + 1; i < stem.Length; i++)
        {
            var c = stem[i];
            if (c is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
                return false;
        }

        return IsStoredName(stem[..dot]);
    }

    private readonly record struct StoredEntry(string Path, long Length, DateTime WrittenUtc, bool Stored);
}

public readonly record struct StoredFile(string Path, string ContentType, int MaxAgeSeconds);

public readonly record struct SavedImage(string Name, DateTime ExpiresAtUtc);
