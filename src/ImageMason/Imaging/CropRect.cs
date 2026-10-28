using System.Globalization;

namespace ImageMason.Imaging;

/// <summary>The area of an image to keep, in display pixels (after the orientation is applied).</summary>
public sealed record CropRect(int Left, int Top, int Width, int Height)
{
    /// <summary>Whether the area lies inside an image displayed at <paramref name="width"/> x <paramref name="height"/>.</summary>
    public bool FitsWithin(int width, int height) => (long)Left + Width <= width && (long)Top + Height <= height;

    /// <summary>Whether the area is the whole image, so there is nothing to cut.</summary>
    public bool Covers(int width, int height) => Left == 0 && Top == 0 && Width == width && Height == height;

    /// <summary>vips extract_area arguments, after the input and output: left top width height.</summary>
    public IEnumerable<string> VipsArguments() => [Format(Left), Format(Top), Format(Width), Format(Height)];

    /// <summary>The crop parameter's form: left,top,width,height.</summary>
    public override string ToString() => $"{Format(Left)},{Format(Top)},{Format(Width)},{Format(Height)}";

    private static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);
}
