using System.Buffers.Binary;

namespace ImageMason.Imaging;

/// <summary>
/// Walks a RIFF file's chunks (WebP) after its 12-byte header (RIFF, size, form type): a 4-character type, a 4-byte
/// little-endian length, the data, and a pad byte after an odd length. Ends at a chunk whose data runs past the end.
/// </summary>
internal ref struct RiffChunks(ReadOnlySpan<byte> riff)
{
    private readonly ReadOnlySpan<byte> _riff = riff;
    private int _offset = 12;

    public ReadOnlySpan<byte> Type { get; private set; }

    public ReadOnlySpan<byte> Data { get; private set; }

    public bool MoveNext()
    {
        if (_offset + 8 > _riff.Length)
            return false;

        var length = BinaryPrimitives.ReadUInt32LittleEndian(_riff[(_offset + 4)..]);
        if (length > (uint)(_riff.Length - _offset - 8))
            return false;

        Type = _riff.Slice(_offset, 4);
        Data = _riff.Slice(_offset + 8, (int)length);
        _offset += 8 + (int)length + (int)(length & 1);
        return true;
    }
}
