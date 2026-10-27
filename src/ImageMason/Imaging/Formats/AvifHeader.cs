using System.Buffers.Binary;

namespace ImageMason.Imaging;

/// <summary>
/// AVIF (ISO BMFF/HEIF): reads the primary item's properties: ispe (dimensions), irot (rotation), pixi (bit depth)
/// and colr (ICC profile). An auxC alpha property anywhere means the image has alpha. Image sequences (the "avis"
/// major brand) are reported as animated.
/// </summary>
internal static class AvifHeader
{
    private readonly record struct Box(string Type, int Start, int Length);

    private readonly record struct Properties(
        int Width, int Height, int Rotation, int BitDepth, bool Alpha, bool Icc, bool Animated);

    public static ImageInfo Read(ReadOnlySpan<byte> data)
    {
        var p = ReadProperties(data);
        // Odd quarter turns display width and height swapped, like EXIF orientations 5-8.
        return new(p.Width, p.Height, p.Rotation % 2 == 1 ? 6 : 1, p.Animated);
    }

    public static ImageDetails ReadDetails(ReadOnlySpan<byte> data)
    {
        var p = ReadProperties(data);
        return new(p.BitDepth, p.Alpha, p.Icc);
    }

    private static Properties ReadProperties(ReadOnlySpan<byte> data)
    {
        var animated = data.Length >= 12 && data[8..12].SequenceEqual("avis"u8);
        var empty = new Properties(0, 0, 0, 8, false, false, animated);

        // meta is a full box: 4 bytes of version and flags before its children.
        if (Find(ListBoxes(data, 0, data.Length), "meta") is not { Length: >= 4 } meta)
            return empty;

        var children = ListBoxes(data, meta.Start + 4, meta.Length - 4);
        if (Find(children, "iprp") is not { } iprp)
            return empty;

        var iprpChildren = ListBoxes(data, iprp.Start, iprp.Length);
        if (Find(iprpChildren, "ipco") is not { } ipco)
            return empty;

        var properties = ListBoxes(data, ipco.Start, ipco.Length);
        var primary = Find(children, "pitm") is { } pitm ? ReadPrimaryItem(data.Slice(pitm.Start, pitm.Length)) : -1;
        var associated = primary >= 0 && Find(iprpChildren, "ipma") is { } ipma
            ? ReadAssociations(data.Slice(ipma.Start, ipma.Length), primary)
            : null;

        int width = 0, height = 0, rotation = 0, bitDepth = 8;
        bool alpha = false, icc = false;
        for (var index = 0; index < properties.Count; index++)
        {
            var (type, start, length) = properties[index];
            var body = data.Slice(start, length);

            // The alpha plane is its own item, so its auxC isn't associated with the primary item.
            if (type == "auxC" && body.Length > 4 && body[4..].StartsWith("urn:mpeg:mpegB:cicp:systems:auxiliary:alpha"u8))
                alpha = true;

            // ipma indices are 1-based; without associations, take the largest ispe (the primary image or grid).
            if (associated is not null && !associated.Contains(index + 1))
                continue;

            switch (type)
            {
                case "ispe" when body.Length >= 12:
                    var w = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(body[4..]), int.MaxValue);
                    var h = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(body[8..]), int.MaxValue);
                    if ((long)w * h > (long)width * height)
                        (width, height) = (w, h);
                    break;
                case "irot" when body.Length >= 1:
                    rotation = body[0] & 0x03;
                    break;
                case "pixi" when body.Length >= 6 && body[4] > 0:
                    bitDepth = body[5];
                    break;
                case "colr" when body.Length >= 4:
                    icc |= body[..4].SequenceEqual("prof"u8) || body[..4].SequenceEqual("rICC"u8);
                    break;
            }
        }

        return new(width, height, rotation, bitDepth, alpha, icc, animated);
    }

    private static int ReadPrimaryItem(ReadOnlySpan<byte> pitm)
    {
        if (pitm.Length < 6)
            return -1;

        return pitm[0] == 0
            ? BinaryPrimitives.ReadUInt16BigEndian(pitm[4..])
            : pitm.Length >= 8 ? (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(pitm[4..]), int.MaxValue) : -1;
    }

    // ipma: entry_count, then per item: item_ID, association count, and 7- or 15-bit property indices.
    private static HashSet<int>? ReadAssociations(ReadOnlySpan<byte> ipma, int itemId)
    {
        if (ipma.Length < 8)
            return null;

        var version = ipma[0];
        var wideIndices = (ipma[3] & 1) != 0;
        var count = BinaryPrimitives.ReadUInt32BigEndian(ipma[4..]);
        var offset = 8;

        for (var entry = 0u; entry < count; entry++)
        {
            var idSize = version < 1 ? 2 : 4;
            if (offset + idSize + 1 > ipma.Length)
                return null;

            var id = version < 1
                ? BinaryPrimitives.ReadUInt16BigEndian(ipma[offset..])
                : (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(ipma[offset..]), int.MaxValue);
            offset += idSize;
            int associations = ipma[offset++];

            var indexSize = wideIndices ? 2 : 1;
            if (offset + associations * indexSize > ipma.Length)
                return null;

            if (id == itemId)
            {
                var indices = new HashSet<int>();
                for (var a = 0; a < associations; a++)
                {
                    var index = wideIndices
                        ? BinaryPrimitives.ReadUInt16BigEndian(ipma[(offset + a * 2)..]) & 0x7FFF
                        : ipma[offset + a] & 0x7F;
                    indices.Add(index);
                }

                return indices;
            }

            offset += associations * indexSize;
        }

        return null;
    }

    private static Box? Find(List<Box> boxes, string type)
    {
        foreach (var box in boxes)
        {
            if (box.Type == type)
                return box;
        }

        return null;
    }

    // Each box's type, and its body as an offset and length into the whole file. At most 1024: an image has a few dozen.
    private static List<Box> ListBoxes(ReadOnlySpan<byte> data, int start, int length)
    {
        var list = new List<Box>();
        var boxes = new BmffBoxes(data, start, length);
        while (list.Count < 1024 && boxes.MoveNext())
            list.Add(new Box(System.Text.Encoding.ASCII.GetString(boxes.Type), boxes.Start, boxes.Body.Length));

        return list;
    }
}
