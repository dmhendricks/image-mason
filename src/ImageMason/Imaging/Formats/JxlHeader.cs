using System.Buffers.Binary;

namespace ImageMason.Imaging;

/// <summary>
/// JPEG XL: a bare codestream (FF 0A) or one inside the ISO BMFF container (a jxlc box, or jxlp parts). Reads the
/// codestream's SizeHeader and ImageMetadata, which are bit-packed: dimensions, orientation, animation, bit depth,
/// alpha (an alpha extra channel) and whether the color space is an ICC profile. The field layouts follow ISO/IEC
/// 18181-1, as libjxl implements them (lib/jxl/headers.cc, image_metadata.cc, color_encoding_internal.cc).
/// </summary>
internal static class JxlHeader
{
    private const int MaxExtraChannels = 256;

    // Enough of a codestream split into jxlp parts to hold the headers, which take a few dozen bytes.
    private const int MaxPartialBytes = 64 * 1024;

    private readonly record struct Metadata(
        int Width, int Height, int Orientation, bool Animated, int BitDepth, bool Alpha, bool Icc);

    public static ImageInfo Read(ReadOnlySpan<byte> data) =>
        ReadMetadata(data) is { } m ? new(m.Width, m.Height, m.Orientation, m.Animated) : default;

    public static ImageDetails ReadDetails(ReadOnlySpan<byte> data) =>
        ReadMetadata(data) is { } m ? new(m.BitDepth, m.Alpha, m.Icc) : new(8, false, false);

    private static Metadata? ReadMetadata(ReadOnlySpan<byte> data)
    {
        var codestream = Codestream(data);
        if (codestream.Length < 3 || codestream[0] != 0xFF || codestream[1] != 0x0A)
            return null;

        var bits = new BitReader(codestream[2..]);
        var (width, height) = ReadSize(ref bits);

        int orientation = 1, bitDepth = 8;
        bool animated = false, alpha = false, icc = false;

        // ImageMetadata: all_default leaves everything at its default (upright, 8-bit, no extra channels, sRGB).
        if (!bits.Bool())
        {
            // extra_fields
            if (bits.Bool())
            {
                orientation = (int)bits.Bits(3) + 1;
                if (bits.Bool())
                    ReadSize(ref bits); // intrinsic size
                if (bits.Bool())
                    SkipPreview(ref bits);
                animated = bits.Bool();
                if (animated)
                    SkipAnimation(ref bits);
            }

            bitDepth = ReadBitDepth(ref bits);
            bits.Bool(); // modular_16_bit_buffer_sufficient

            var extraChannels = bits.U32(new(0, 0), new(0, 1), new(4, 2), new(12, 1));
            if (extraChannels > MaxExtraChannels)
                return null;
            for (var i = 0; i < extraChannels; i++)
                alpha |= ReadExtraChannelIsAlpha(ref bits);

            bits.Bool(); // xyb_encoded

            // ColorEncoding: all_default is sRGB; otherwise want_icc says the color space is an embedded profile.
            if (!bits.Bool())
                icc = bits.Bool();
        }

        if (bits.Overrun || width is <= 0 or > int.MaxValue || height is <= 0 or > int.MaxValue)
            return null;

        return new((int)width, (int)height, orientation, animated, bitDepth, alpha, icc);
    }

    /// <summary>The codestream, from a bare file or from the container's jxlc box or (joined) jxlp parts.</summary>
    private static ReadOnlySpan<byte> Codestream(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith((ReadOnlySpan<byte>)[0xFF, 0x0A]))
            return data;

        List<byte>? parts = null;
        var boxes = new BmffBoxes(data);
        while (boxes.MoveNext())
        {
            var body = boxes.Body;
            if (boxes.Type.SequenceEqual("jxlc"u8))
                return body;

            // jxlp: a 4-byte part index, then the next piece of the codestream.
            if (boxes.Type.SequenceEqual("jxlp"u8) && body.Length > 4)
            {
                parts ??= [];
                foreach (var b in body[4..][..Math.Min(body.Length - 4, MaxPartialBytes - parts.Count)])
                    parts.Add(b);
                if (parts.Count >= MaxPartialBytes)
                    break;
            }
        }

        return parts is null ? [] : parts.ToArray();
    }

    // SizeHeader: small sizes are multiples of 8 in 5 bits; the width is either stored or a fixed ratio of the height.
    private static (long Width, long Height) ReadSize(ref BitReader bits)
    {
        var small = bits.Bool();
        long height = small ? (bits.Bits(5) + 1) * 8L : bits.U32(new(9, 1), new(13, 1), new(18, 1), new(30, 1));
        var ratio = bits.Bits(3);
        long width = ratio != 0
            ? AspectWidth(height, ratio)
            : small ? (bits.Bits(5) + 1) * 8L : bits.U32(new(9, 1), new(13, 1), new(18, 1), new(30, 1));
        return (width, height);
    }

    private static long AspectWidth(long height, uint ratio) => ratio switch
    {
        1 => height,
        2 => height * 12 / 10,
        3 => height * 4 / 3,
        4 => height * 3 / 2,
        5 => height * 16 / 9,
        6 => height * 5 / 4,
        _ => height * 2
    };

    private static void SkipPreview(ref BitReader bits)
    {
        var div8 = bits.Bool();
        SkipPreviewSide(ref bits, div8);
        if (bits.Bits(3) == 0)
            SkipPreviewSide(ref bits, div8);
    }

    private static void SkipPreviewSide(ref BitReader bits, bool div8)
    {
        if (div8)
            bits.U32(new(0, 16), new(0, 32), new(5, 1), new(9, 33));
        else
            bits.U32(new(6, 1), new(8, 65), new(10, 321), new(12, 1345));
    }

    // AnimationHeader: ticks per second (numerator, denominator), loop count, have_timecodes.
    private static void SkipAnimation(ref BitReader bits)
    {
        bits.U32(new(0, 100), new(0, 1000), new(10, 1), new(30, 1));
        bits.U32(new(0, 1), new(0, 1001), new(8, 1), new(10, 1));
        bits.U32(new(0, 0), new(3, 0), new(16, 0), new(32, 0));
        bits.Bool();
    }

    private static int ReadBitDepth(ref BitReader bits)
    {
        if (!bits.Bool())
            return (int)bits.U32(new(0, 8), new(0, 10), new(0, 12), new(6, 1));

        // Floating-point samples: bits per sample, then exponent bits.
        var depth = (int)bits.U32(new(0, 32), new(0, 16), new(0, 24), new(6, 1));
        bits.Bits(4);
        return depth;
    }

    // ExtraChannelInfo: all_default is an alpha channel; otherwise type, bit depth, dim shift, name and type fields.
    private static bool ReadExtraChannelIsAlpha(ref BitReader bits)
    {
        if (bits.Bool())
            return true;

        var type = bits.U32(new(0, 0), new(0, 1), new(4, 2), new(6, 18));
        ReadBitDepth(ref bits);
        bits.U32(new(0, 0), new(0, 3), new(0, 4), new(3, 1)); // dim_shift
        var nameLength = bits.U32(new(0, 0), new(4, 0), new(5, 16), new(10, 48));
        for (var i = 0; i < nameLength; i++)
            bits.Bits(8);

        switch (type)
        {
            case 0: // alpha: alpha_associated
                bits.Bool();
                return true;
            case 2: // spot color: four 16-bit floats
                for (var i = 0; i < 4; i++)
                    bits.Bits(16);
                break;
            case 5: // CFA: cfa_channel
                bits.U32(new(0, 1), new(2, 0), new(4, 3), new(8, 19));
                break;
        }

        return false;
    }

    /// <summary>One U32 distribution: read <paramref name="Count"/> bits and add <paramref name="Offset"/> (0 bits: a constant).</summary>
    private readonly record struct Distribution(int Count, uint Offset);

    /// <summary>Reads bits least significant first, as JPEG XL stores them. Reading past the end sets Overrun.</summary>
    private ref struct BitReader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private long _position;

        public bool Overrun { get; private set; }

        public uint Bits(int count)
        {
            uint value = 0;
            for (var i = 0; i < count; i++)
            {
                var index = _position >> 3;
                if (index >= _data.Length)
                {
                    Overrun = true;
                    return 0;
                }

                value |= (uint)((_data[(int)index] >> (int)(_position & 7)) & 1) << i;
                _position++;
            }

            return value;
        }

        public bool Bool() => Bits(1) == 1;

        /// <summary>A 2-bit selector picks one of four distributions.</summary>
        public uint U32(Distribution d0, Distribution d1, Distribution d2, Distribution d3)
        {
            var d = Bits(2) switch { 0 => d0, 1 => d1, 2 => d2, _ => d3 };
            return d.Count == 0 ? d.Offset : Bits(d.Count) + d.Offset;
        }
    }
}
