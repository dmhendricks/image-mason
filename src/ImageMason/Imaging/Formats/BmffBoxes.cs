using System.Buffers.Binary;

namespace ImageMason.Imaging;

/// <summary>
/// Walks ISO BMFF boxes (AVIF, the JPEG XL container) in part of a file: a 32-bit size (1 = a 64-bit size follows,
/// 0 = to the end), then a 4-character type. Ends at a box that runs past the end. Boxes inside a box are walked by
/// starting another walker at its <see cref="Start"/>.
/// </summary>
internal ref struct BmffBoxes(ReadOnlySpan<byte> data, int start, int length)
{
    private readonly ReadOnlySpan<byte> _data = data;
    private readonly int _end = start + length;
    private int _offset = start;

    public BmffBoxes(ReadOnlySpan<byte> data) : this(data, 0, data.Length)
    {
    }

    public ReadOnlySpan<byte> Type { get; private set; }

    /// <summary>Where the box's body starts in the whole file.</summary>
    public int Start { get; private set; }

    public ReadOnlySpan<byte> Body { get; private set; }

    public bool MoveNext()
    {
        if (_offset + 8 > _end)
            return false;

        long size = BinaryPrimitives.ReadUInt32BigEndian(_data[_offset..]);
        var header = 8;
        if (size == 1)
        {
            if (_offset + 16 > _end)
                return false;
            size = (long)Math.Min(BinaryPrimitives.ReadUInt64BigEndian(_data[(_offset + 8)..]), long.MaxValue);
            header = 16;
        }
        else if (size == 0)
        {
            size = _end - _offset;
        }

        if (size < header || size > _end - _offset)
            return false;

        Type = _data.Slice(_offset + 4, 4);
        Start = _offset + header;
        Body = _data.Slice(Start, (int)size - header);
        _offset += (int)size;
        return true;
    }
}
