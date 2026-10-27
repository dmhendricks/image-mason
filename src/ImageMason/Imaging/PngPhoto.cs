using System.Buffers.Binary;
using System.IO.Compression;

namespace ImageMason.Imaging;

/// <summary>
/// True when a PNG is a photo that would store better as a JPEG. Graphics, transparency, and JPEG inputs are false.
/// </summary>
public static class PngPhoto
{
    private const int SampleAxis = 128;
    private const int PhotoColorCount = 4096;

    // Rows decoded between cancellation checks.
    private const int RowsPerCheck = 64;

    /// <param name="maxPixels">Images over this (IMAGE_MASON_MAX_PIXELS) are rejected before any pixels are decoded,
    /// since the check inflates every row of an RGBA image.</param>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled while rows were decoded.</exception>
    public static bool IsPhoto(ReadOnlySpan<byte> data, long maxPixels, CancellationToken ct)
    {
        if (!data.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
            return false;

        try
        {
            return CountColors(data, maxPixels, ct);
        }
        catch (InvalidDataException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool CountColors(ReadOnlySpan<byte> png, long maxPixels, CancellationToken ct)
    {
        int width = 0, height = 0, bitDepth = 0, colorType = 0, interlace = 0;
        var hasTransparency = false;
        using var idat = new MemoryStream();

        var chunks = new PngChunks(png);
        while (chunks.MoveNext())
        {
            // A cut-off chunk means a damaged file, but fewer than 12 bytes left is just the end.
            if (!chunks.Complete)
            {
                if (chunks.Offset + 12 > png.Length)
                    break;
                return false;
            }

            var type = chunks.Type;
            var chunk = chunks.Data;
            if (type.SequenceEqual("IHDR"u8))
            {
                if (chunk.Length < 13)
                    return false;
                width = (int)BinaryPrimitives.ReadUInt32BigEndian(chunk);
                height = (int)BinaryPrimitives.ReadUInt32BigEndian(chunk[4..]);
                bitDepth = chunk[8];
                colorType = chunk[9];
                interlace = chunk[12];
            }
            else if (type.SequenceEqual("tRNS"u8))
            {
                hasTransparency = true;
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                idat.Write(chunk);
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                break;
            }
        }

        // Over the pixel limit (optimize rejects it anyway), indexed, grayscale, interlaced, or an explicit
        // transparency key. JPEG cannot keep transparency.
        if (width <= 0 || height <= 0 || (long)width * height > maxPixels)
            return false;
        if (interlace != 0 || hasTransparency || colorType is not (2 or 6))
            return false;
        if (bitDepth is not (8 or 16))
            return false;

        var channels = colorType == 6 ? 4 : 3;
        var bytesPerChannel = bitDepth / 8;
        var bytesPerPixel = channels * bytesPerChannel;
        var rowLength = (long)width * bytesPerPixel;
        if (rowLength is <= 0 or > 1_000_000)
            return false;
        var rowBytes = (int)rowLength;
        var strideX = Math.Max(1, width / SampleAxis);
        var strideY = Math.Max(1, height / SampleAxis);
        var sampleCount = (((width - 1) / strideX) + 1) * (((height - 1) / strideY) + 1);
        var cutoff = Math.Min(PhotoColorCount, Math.Max(256, sampleCount / 2));

        var colors = new HashSet<int>();
        var prior = new byte[rowBytes];
        var row = new byte[rowBytes];
        var enoughColors = false;
        idat.Position = 0;
        using var pixels = new ZLibStream(idat, CompressionMode.Decompress, leaveOpen: true);

        for (var y = 0; y < height; y++)
        {
            if (y % RowsPerCheck == 0)
                ct.ThrowIfCancellationRequested();

            var filterByte = pixels.ReadByte();
            if (filterByte < 0 || !ReadExactly(pixels, row))
                return false;
            if (filterByte > 4)
                return false;

            Reconstruct((byte)filterByte, row, prior, bytesPerPixel);

            if (channels == 4 && RowHasTransparency(row, width, bytesPerPixel, bytesPerChannel))
                return false;

            if (!enoughColors && y % strideY == 0)
            {
                for (var x = 0; x < width; x += strideX)
                {
                    colors.Add(PackRgb(row, x * bytesPerPixel, bytesPerChannel));
                    if (colors.Count >= cutoff)
                    {
                        enoughColors = true;
                        break;
                    }
                }
            }

            (prior, row) = (row, prior);
            if (enoughColors && channels == 3)
                return true;
        }

        return enoughColors;
    }

    private static bool RowHasTransparency(byte[] row, int width, int bytesPerPixel, int bytesPerChannel)
    {
        var alpha = 3 * bytesPerChannel;
        for (var x = 0; x < width; x++)
        {
            var i = x * bytesPerPixel + alpha;
            if (row[i] != 255)
                return true;
            if (bytesPerChannel == 2 && row[i + 1] != 255)
                return true;
        }

        return false;
    }

    private static int PackRgb(byte[] row, int pixel, int bytesPerChannel)
    {
        if (bytesPerChannel == 1)
            return (row[pixel] << 16) | (row[pixel + 1] << 8) | row[pixel + 2];

        return (row[pixel] << 16) | (row[pixel + 2] << 8) | row[pixel + 4];
    }

    private static void Reconstruct(byte filter, byte[] row, byte[] prior, int bytesPerPixel)
    {
        switch (filter)
        {
            case 0:
                break;
            case 1:
                for (var i = bytesPerPixel; i < row.Length; i++)
                    row[i] = (byte)(row[i] + row[i - bytesPerPixel]);
                break;
            case 2:
                for (var i = 0; i < row.Length; i++)
                    row[i] = (byte)(row[i] + prior[i]);
                break;
            case 3:
                for (var i = 0; i < row.Length; i++)
                {
                    var left = i >= bytesPerPixel ? row[i - bytesPerPixel] : 0;
                    row[i] = (byte)(row[i] + ((left + prior[i]) / 2));
                }
                break;
            case 4:
                for (var i = 0; i < row.Length; i++)
                {
                    var left = i >= bytesPerPixel ? row[i - bytesPerPixel] : (byte)0;
                    var upLeft = i >= bytesPerPixel ? prior[i - bytesPerPixel] : (byte)0;
                    row[i] = (byte)(row[i] + Paeth(left, prior[i], upLeft));
                }
                break;
        }
    }

    private static byte Paeth(byte left, byte up, byte upLeft)
    {
        var estimate = left + up - upLeft;
        var leftDistance = Math.Abs(estimate - left);
        var upDistance = Math.Abs(estimate - up);
        var upLeftDistance = Math.Abs(estimate - upLeft);
        if (leftDistance <= upDistance && leftDistance <= upLeftDistance)
            return left;
        if (upDistance <= upLeftDistance)
            return up;
        return upLeft;
    }

    private static bool ReadExactly(Stream stream, byte[] buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = stream.Read(buffer, read, buffer.Length - read);
            if (n == 0)
                return false;
            read += n;
        }

        return true;
    }
}
