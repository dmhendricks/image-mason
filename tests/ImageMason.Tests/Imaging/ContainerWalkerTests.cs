using System.Buffers.Binary;
using System.Text;
using ImageMason.Imaging;

namespace ImageMason.Tests.Imaging;

public sealed class ContainerWalkerTests
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static byte[] PngChunk(string type, int length, int crc = 4)
    {
        var chunk = new byte[8 + length + crc];
        BinaryPrimitives.WriteUInt32BigEndian(chunk, (uint)length);
        Encoding.ASCII.GetBytes(type).CopyTo(chunk, 4);
        return chunk;
    }

    private static byte[] RiffChunk(string type, string data)
    {
        var bytes = Encoding.ASCII.GetBytes(data);
        var chunk = new byte[8 + bytes.Length + (bytes.Length & 1)];
        Encoding.ASCII.GetBytes(type).CopyTo(chunk, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4), (uint)bytes.Length);
        bytes.CopyTo(chunk, 8);
        return chunk;
    }

    private static byte[] Box(string type, byte[] body, int size = -1)
    {
        var box = new byte[8 + body.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, size < 0 ? (uint)box.Length : (uint)size);
        Encoding.ASCII.GetBytes(type).CopyTo(box, 4);
        body.CopyTo(box, 8);
        return box;
    }

    private static byte[] LargeBox(string type, byte[] body)
    {
        var box = new byte[16 + body.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, 1);
        Encoding.ASCII.GetBytes(type).CopyTo(box, 4);
        BinaryPrimitives.WriteUInt64BigEndian(box.AsSpan(8), (ulong)box.Length);
        body.CopyTo(box, 16);
        return box;
    }

    private static List<string> PngWalk(byte[] png)
    {
        var seen = new List<string>();
        var chunks = new PngChunks(png);
        while (chunks.MoveNext())
            seen.Add($"{Encoding.ASCII.GetString(chunks.Type)}:{chunks.Data.Length}:{(chunks.Complete ? "complete" : "cut")}");
        return seen;
    }

    [Fact]
    public void PngChunks_ReturnsACutOffChunkAsIncomplete()
    {
        byte[] png = [.. PngSignature, .. PngChunk("IHDR", 13), .. PngChunk("iCCP", 100)[..50]];

        Assert.Equal(["IHDR:13:complete", "iCCP:42:cut"], PngWalk(png));
    }

    [Fact]
    public void PngChunks_NeedsTheCrcForAChunkToBeComplete()
    {
        byte[] png = [.. PngSignature, .. PngChunk("IEND", 0, crc: 2)];

        Assert.Equal(["IEND:0:cut"], PngWalk(png));
    }

    [Fact]
    public void PngChunks_StopsAfterALengthPastAnyFile()
    {
        var huge = PngChunk("zTXt", 0);
        BinaryPrimitives.WriteUInt32BigEndian(huge, uint.MaxValue);
        byte[] png = [.. PngSignature, .. huge, .. PngChunk("IEND", 0)];

        // Its data is the rest of the file (its own 4 bytes and the IEND chunk), and nothing comes after it.
        Assert.Equal(["zTXt:16:cut"], PngWalk(png));
    }

    [Fact]
    public void RiffChunks_SkipsThePadByteAfterAnOddLength()
    {
        byte[] webp = [.. "RIFF\0\0\0\0WEBP"u8, .. RiffChunk("ODD ", "abc"), .. RiffChunk("EXIF", "tiff")];

        var seen = new List<string>();
        var chunks = new RiffChunks(webp);
        while (chunks.MoveNext())
            seen.Add($"{Encoding.ASCII.GetString(chunks.Type)}={Encoding.ASCII.GetString(chunks.Data)}");

        Assert.Equal(["ODD =abc", "EXIF=tiff"], seen);
    }

    [Fact]
    public void RiffChunks_StopsAtAChunkPastTheEnd()
    {
        byte[] webp = [.. "RIFF\0\0\0\0WEBP"u8, .. RiffChunk("VP8 ", "ab"), .. RiffChunk("EXIF", "tiff")[..10]];

        var count = 0;
        var chunks = new RiffChunks(webp);
        while (chunks.MoveNext())
            count++;

        Assert.Equal(1, count);
    }

    [Fact]
    public void BmffBoxes_ReadsSizeZeroAndLargeSizes_AndBoxesInsideBoxes()
    {
        byte[] file = [.. Box("ftyp", "avif"u8.ToArray()), .. LargeBox("meta", Box("pitm", [1, 2])), .. Box("mdat", [9, 9, 9], size: 0)];

        var seen = new List<string>();
        var boxes = new BmffBoxes(file);
        while (boxes.MoveNext())
        {
            seen.Add($"{Encoding.ASCII.GetString(boxes.Type)}:{boxes.Body.Length}");
            if (boxes.Type.SequenceEqual("meta"u8))
            {
                var inner = new BmffBoxes(file, boxes.Start, boxes.Body.Length);
                while (inner.MoveNext())
                    seen.Add($"  {Encoding.ASCII.GetString(inner.Type)}:{inner.Body.Length} at {inner.Start}");
            }
        }

        Assert.Equal(["ftyp:4", "meta:10", "  pitm:2 at 36", "mdat:3"], seen);
    }

    [Fact]
    public void BmffBoxes_StopsAtABoxPastTheEnd()
    {
        byte[] file = [.. Box("ftyp", "avif"u8.ToArray()), .. Box("meta", new byte[20])[..16]];

        var count = 0;
        var boxes = new BmffBoxes(file);
        while (boxes.MoveNext())
            count++;

        Assert.Equal(1, count);
    }
}
