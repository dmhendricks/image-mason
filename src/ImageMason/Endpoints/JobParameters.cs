namespace ImageMason.Endpoints;

/// <summary>
/// The query parameters optimize, convert, resize and crop share, bound with [AsParameters]. Each endpoint checks
/// format and rect itself (convert and crop require them); <see cref="OptimizeEndpoint.RunAsync"/> checks the rest.
/// Properties rather than a primary constructor: the Request Delegate Generator can't bind a constructor's
/// nullable strings.
/// </summary>
internal sealed record JobParameters
{
    public string? Format { get; init; }
    public string? Quality { get; init; }
    public double? MinScore { get; init; }
    public string? Rect { get; init; }
    public string? Output { get; init; }
    public string? OutputFilename { get; init; }
    public string? Url { get; init; }
    public string? Source { get; init; }

    /// <summary>Nullable so it's optional, as a bool parameter defaulting to false would be; null means false.</summary>
    public bool? KeepOriginal { get; init; }
}
