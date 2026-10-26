using ImageMason.Imaging;

namespace ImageMason.Endpoints;

/// <summary>The width, height, fit, crop and upscale parameters shared by optimize and resize, bound with [AsParameters].</summary>
internal sealed record ResizeParameters
{
    public const int MaxSide = 65535;

    public int? Width { get; init; }
    public int? Height { get; init; }
    public string? Fit { get; init; }
    public string? Crop { get; init; }
    public bool? Upscale { get; init; }

    /// <summary>
    /// Builds the box to resize to, or returns why the parameters can't be used. With no width or height the
    /// box is null, and fit, crop and upscale must not be sent.
    /// </summary>
    public bool TryParse(out ResizeRequest? resize, out string? error)
    {
        resize = null;
        error = null;

        if (Width is < 1 or > MaxSide || Height is < 1 or > MaxSide)
            error = $"width and height must be between 1 and {MaxSide}.";
        else if (Width is null && Height is null)
        {
            if (!string.IsNullOrWhiteSpace(Fit) || !string.IsNullOrWhiteSpace(Crop) || Upscale is not null)
                error = "fit, crop and upscale need width, height or both.";
        }
        else if (!TryParseFit(Fit, out var resizeFit))
            error = "fit must be 'contain', 'cover' or 'fill'.";
        else if (resizeFit != ResizeFit.Contain && (Width is null || Height is null))
            error = $"fit={resizeFit.ToString().ToLowerInvariant()} needs both width and height.";
        else if (!TryParseCrop(Crop, out var position))
            error = "crop must be 'center', 'attention' or 'entropy'.";
        else if (!string.IsNullOrWhiteSpace(Crop) && resizeFit != ResizeFit.Cover)
            error = "crop is only used with fit=cover.";
        else
            resize = new ResizeRequest(Width, Height, resizeFit, Upscale ?? false, position);

        return error is null;
    }

    private static bool TryParseFit(string? value, out ResizeFit fit)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case null or "" or "contain":
                fit = ResizeFit.Contain;
                return true;
            case "cover":
                fit = ResizeFit.Cover;
                return true;
            case "fill":
                fit = ResizeFit.Fill;
                return true;
            default:
                fit = default;
                return false;
        }
    }

    private static bool TryParseCrop(string? value, out CropPosition crop)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case null or "" or "center" or "centre":
                crop = CropPosition.Center;
                return true;
            case "attention":
                crop = CropPosition.Attention;
                return true;
            case "entropy":
                crop = CropPosition.Entropy;
                return true;
            default:
                crop = default;
                return false;
        }
    }
}
