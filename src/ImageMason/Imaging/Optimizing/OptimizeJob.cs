namespace ImageMason.Imaging;

/// <param name="Work">The job's folder for work files.</param>
/// <param name="Reference">The pixels results are scored against: the source, or a normalized PNG.</param>
/// <param name="Input">The pixels results are encoded from: the reference, or the same pixels with an alpha channel
/// that the reference dropped because it is opaque everywhere.</param>
/// <param name="Cropped">Cropped; method names start with "crop+".</param>
/// <param name="Resized">Resized; method names start with "resize+" (after "crop+").</param>
/// <param name="Icc">The reference embeds an ICC profile, which the encoders copy.</param>
/// <param name="Scorer">Scores candidates against the reference.</param>
internal sealed record OptimizeJob(string Work, string Source, string Reference, string Input, ImageInfo Info, bool Cropped,
    bool Resized, double? MinScore, bool Icc, Scorer Scorer)
{
    public string MethodName(string method) => (Cropped ? "crop+" : "") + (Resized ? "resize+" : "") + method;
}

/// <summary>An encoded result; the smallest of a job's candidates wins.</summary>
/// <param name="Score">SSIMULACRA2 against the job's reference; null when not scored, or when scoring failed.</param>
internal sealed record Candidate(string Method, string Path, long Size, double? Score)
{
    public static Candidate FromFile(string method, string path, double? score = null) => new(method, path, new FileInfo(path).Length, score);
}
