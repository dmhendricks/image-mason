using System.Globalization;
using ImageMason.Imaging;

namespace ImageMason.Endpoints;

/// <summary>transform's crop parameter: the area to keep, as left,top,width,height in display pixels.</summary>
internal static class CropParameter
{
    /// <summary>
    /// Parses the area to keep, or returns why it can't be used. Empty means no crop. Whether the area fits the
    /// image is checked later, once its dimensions are known.
    /// </summary>
    public static bool TryParse(string? value, out CropRect? rect, out string? error)
    {
        rect = null;
        error = null;
        if (string.IsNullOrWhiteSpace(value))
            return true;

        var parts = value.Split(',');
        var numbers = new int[parts.Length];
        var valid = parts.Length == 4;
        for (var i = 0; valid && i < parts.Length; i++)
            valid = int.TryParse(parts[i].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]);

        if (!valid)
            error = "crop must be left,top,width,height in whole pixels, such as 80,0,480,480.";
        else if (numbers[2] is < 1 or > ResizeParameters.MaxSide || numbers[3] is < 1 or > ResizeParameters.MaxSide)
            error = $"crop's width and height must be between 1 and {ResizeParameters.MaxSide}.";
        else
            rect = new CropRect(numbers[0], numbers[1], numbers[2], numbers[3]);

        return error is null;
    }
}
