using System.Globalization;

namespace ImageMason.Imaging;

public enum ResizeFit
{
    /// <summary>Fit inside the box, keeping the aspect ratio. Either side may be left out.</summary>
    Contain,

    /// <summary>Fill the box, keeping the aspect ratio, and crop what overflows.</summary>
    Cover,

    /// <summary>Stretch to exactly the box, ignoring the aspect ratio.</summary>
    Fill
}

/// <summary>Which part of the image fit=cover keeps (vipsthumbnail --smartcrop).</summary>
public enum CropPosition
{
    Center,

    /// <summary>The most interesting area: skin tones, saturated colors and edges.</summary>
    Attention,

    /// <summary>The area with the most detail.</summary>
    Entropy
}

/// <summary>A target box in display pixels (after the orientation is applied).</summary>
/// <param name="Width">Box width; null leaves it free (contain only).</param>
/// <param name="Height">Box height; null leaves it free (contain only).</param>
/// <param name="Upscale">Enlarge images smaller than the box. On by default; fill always reaches the exact size.</param>
/// <param name="Position">For cover: which part of the image to keep.</param>
public sealed record ResizeRequest(int? Width, int? Height, ResizeFit Fit = ResizeFit.Contain, bool Upscale = true,
    CropPosition Position = CropPosition.Center)
{
    /// <summary>
    /// The size vipsthumbnail produces for an image displayed at <paramref name="width"/> x <paramref name="height"/>.
    /// Used to decide whether anything changes and to size the memory reservation; the result may differ by a
    /// pixel from vips' rounding.
    /// </summary>
    public (int Width, int Height) OutputSize(int width, int height)
    {
        if (Fit == ResizeFit.Fill)
            return (Width ?? width, Height ?? height);

        var scaleX = Width is { } w ? (double)w / width : double.PositiveInfinity;
        var scaleY = Height is { } h ? (double)h / height : double.PositiveInfinity;
        var scale = Fit == ResizeFit.Cover ? Math.Max(scaleX, scaleY) : Math.Min(scaleX, scaleY);
        if (!Upscale || double.IsInfinity(scale))
            scale = Math.Min(scale, 1);

        var scaledWidth = Math.Max(1, (int)Math.Round(width * scale));
        var scaledHeight = Math.Max(1, (int)Math.Round(height * scale));
        return Fit == ResizeFit.Cover
            ? (Math.Min(Width ?? scaledWidth, scaledWidth), Math.Min(Height ?? scaledHeight, scaledHeight))
            : (scaledWidth, scaledHeight);
    }

    /// <summary>vipsthumbnail arguments: --size (">" shrinks only, "!" forces the size) and --smartcrop for cover.</summary>
    public IEnumerable<string> VipsArguments()
    {
        var box = $"{Format(Width)}x{Format(Height)}";
        switch (Fit)
        {
            case ResizeFit.Fill:
                return ["--size", box + "!"];
            case ResizeFit.Cover:
                return ["--size", Upscale ? box : box + ">", "--smartcrop", Position.ToString().ToLowerInvariant().Replace("center", "centre")];
            default:
                return ["--size", Upscale ? box : box + ">"];
        }
    }

    private static string Format(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "";
}
