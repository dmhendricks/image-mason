using System.Buffers.Binary;

namespace ImageMason.Imaging;

/// <summary>
/// Walks a PNG's chunks after the 8-byte signature: a 4-byte length, a 4-byte type, the data, then a CRC (not checked).
/// A chunk whose header is there is returned even when the rest is cut off, as in the first part of a large file:
/// <see cref="Data"/> is then what there is of it, and <see cref="Complete"/> is false. A length that would run past
/// the end of any file ends the walk after that chunk.
/// </summary>
internal ref struct PngChunks(ReadOnlySpan<byte> png)
{
    private readonly ReadOnlySpan<byte> _png = png;
    private long _next = 8;

    /// <summary>Where the chunk starts: its length field.</summary>
    public int Offset { get; private set; }

    public ReadOnlySpan<byte> Type { get; private set; }

    /// <summary>The chunk's data, or as much of it as there is.</summary>
    public ReadOnlySpan<byte> Data { get; private set; }

    /// <summary>The data and the CRC after it are all there.</summary>
    public bool Complete { get; private set; }

    public bool MoveNext()
    {
        if (_next + 8 > _png.Length)
            return false;

        var offset = (int)_next;
        var length = BinaryPrimitives.ReadUInt32BigEndian(_png[offset..]);
        var available = _png.Length - offset - 8;

        Offset = offset;
        Type = _png.Slice(offset + 4, 4);
        Data = _png.Slice(offset + 8, (int)Math.Min(length, (uint)available));
        Complete = available >= 4 && length <= (uint)(available - 4);
        _next = offset + 12L + length;
        return true;
    }
}
