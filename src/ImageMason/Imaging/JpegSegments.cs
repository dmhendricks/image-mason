using System.Buffers.Binary;

namespace ImageMason.Imaging;

/// <summary>
/// Walks a JPEG's header segments, from SOI to the first scan, the way libjpeg reads them, so what is measured here is
/// what the tools decode: stray bytes between segments, fill bytes and stuffed zeros are skipped, and TEM and RST0-7,
/// which carry no length, are passed over.
/// </summary>
internal ref struct JpegSegments(ReadOnlySpan<byte> jpeg)
{
    private readonly ReadOnlySpan<byte> _jpeg = jpeg;
    private int _offset = 2;

    public byte Marker { get; private set; }

    /// <summary>The segment's data, after its length.</summary>
    public ReadOnlySpan<byte> Data { get; private set; }

    /// <summary>SOF0-SOF15, excluding DHT (C4), JPG (C8) and DAC (CC): precision, height, width.</summary>
    public readonly bool IsStartOfFrame => Marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC;

    public bool MoveNext()
    {
        while (true)
        {
            while (_offset < _jpeg.Length && _jpeg[_offset] != 0xFF)
                _offset++;
            while (_offset < _jpeg.Length && _jpeg[_offset] == 0xFF)
                _offset++;
            if (_offset >= _jpeg.Length)
                return false;

            var marker = _jpeg[_offset++];
            if (marker is 0x00 or 0x01 or (>= 0xD0 and <= 0xD7))
                continue;

            // Start of scan, end of image, or a second SOI: no more header segments
            if (marker is 0xDA or 0xD9 or 0xD8)
                return false;

            if (_offset + 2 > _jpeg.Length)
                return false;

            var length = BinaryPrimitives.ReadUInt16BigEndian(_jpeg[_offset..]);
            if (length < 2 || _offset + length > _jpeg.Length)
                return false;

            Marker = marker;
            Data = _jpeg.Slice(_offset + 2, length - 2);
            _offset += length;
            return true;
        }
    }
}
