using System.Globalization;

namespace ImageMason.Configuration;

/// <summary>Parses a byte count or a size with a k, m, g, or t suffix. Units are powers of 1024.</summary>
public static class ByteSizes
{
    public static bool TryParse(string? text, out long bytes)
    {
        bytes = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        text = text.Trim();
        var index = 0;
        while (index < text.Length && (char.IsAsciiDigit(text[index]) || text[index] == '.'))
            index++;

        if (index == 0
            || !double.TryParse(text[..index], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)
            || double.IsNaN(number)
            || double.IsInfinity(number))
            return false;

        long multiplier;
        switch (text[index..].Trim().ToLowerInvariant())
        {
            case "" or "b":
                multiplier = 1;
                break;
            case "k" or "kb" or "ki" or "kib":
                multiplier = 1024L;
                break;
            case "m" or "mb" or "mi" or "mib":
                multiplier = 1024L * 1024;
                break;
            case "g" or "gb" or "gi" or "gib":
                multiplier = 1024L * 1024 * 1024;
                break;
            case "t" or "tb" or "ti" or "tib":
                multiplier = 1024L * 1024 * 1024 * 1024;
                break;
            default:
                return false;
        }

        var value = number * multiplier;
        if (value > long.MaxValue)
            return false;

        bytes = (long)Math.Round(value, MidpointRounding.AwayFromZero);
        return true;
    }

    /// <summary>Formats a byte count in GB (powers of 1024) with one decimal, such as "3.8 GB".</summary>
    public static string ToGigabytes(long bytes) =>
        (bytes / (1024d * 1024 * 1024)).ToString("0.0", CultureInfo.InvariantCulture) + " GB";
}
