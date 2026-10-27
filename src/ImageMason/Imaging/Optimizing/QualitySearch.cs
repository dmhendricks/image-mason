using System.Globalization;

namespace ImageMason.Imaging;

/// <summary>One encoder setting searched for the smallest result that meets the floor.</summary>
/// <param name="Smaller">+1 when a higher value gives a smaller file (jpegli distance), -1 when lower does (quality).</param>
/// <param name="Integer">Only whole values are valid.</param>
internal sealed record SearchRange(double Start, double Min, double Max, double Step, double Precision, int Smaller, bool Integer);

/// <summary>
/// Finds the encoder setting with the smallest result that still meets the floor. Steps from a guess toward
/// smaller files while results pass (or toward larger while they fail), doubling the step while the direction
/// holds, until the answer is bracketed; then interpolates between the two scores, which change almost
/// linearly with the setting over a short range. Scoring dominates the cost (about 2 s per attempt at 15 MP),
/// so this typically takes 3-4 attempts.
/// </summary>
/// <param name="maxSteps">The most attempts one search makes.</param>
internal sealed class QualitySearch(int maxSteps, ILogger logger)
{
    /// <param name="encode">Encodes and scores one setting, given a suffix that's unique within the search for its
    /// file names; null when the encoder failed, which ends the search.</param>
    /// <returns>The smallest passing result, or null when none passed.</returns>
    public async Task<Candidate?> SearchAsync(string encoder, SearchRange range, double minScore,
        Func<double, string, Task<Candidate?>> encode, CancellationToken ct)
    {
        double? passing = null, failing = null;
        double passingScore = 0, failingScore = 0;
        var value = range.Start;
        var stride = range.Step;
        bool? lastPassed = null;
        Candidate? best = null;

        for (var step = 0; step < maxSteps; step++)
        {
            ct.ThrowIfCancellationRequested();
            var attempt = await encode(value, step.ToString(CultureInfo.InvariantCulture));
            if (attempt is null)
                break;

            var passed = attempt.Score >= minScore;
            logger.LogDebug("{Encoder} {Value:0.###}: {Bytes} bytes, score {Score:F2} ({Result})",
                encoder, value, attempt.Size, attempt.Score, passed ? "pass" : "fail");

            if (passed)
            {
                if (best is null || attempt.Size < best.Size)
                    best = attempt;
                passing = value;
                passingScore = attempt.Score!.Value;
            }
            else
            {
                failing = value;
                failingScore = attempt.Score ?? 0;
            }

            double next;
            if (passing is { } p && failing is { } f)
            {
                if (Math.Abs(f - p) <= range.Precision)
                    break;

                // Where the floor falls between the two scores, rounded toward the passing side.
                var fraction = passingScore > failingScore ? (minScore - failingScore) / (passingScore - failingScore) : 0.5;
                next = f + Math.Clamp(fraction, 0.1, 0.9) * (p - f);
                if (range.Integer)
                    next = p > f ? Math.Ceiling(next) : Math.Floor(next);
                if (next == p || next == f)
                    next = range.Integer ? (p > f ? p - 1 : p + 1) : (p + f) / 2;
            }
            else
            {
                if (lastPassed == passed)
                    stride *= 2;
                next = value + (passed ? range.Smaller : -range.Smaller) * stride;
            }

            lastPassed = passed;

            next = Math.Clamp(next, range.Min, range.Max);
            if (Math.Abs(next - value) < 0.001 || next == passing || next == failing)
                break;

            value = next;
        }

        return best;
    }
}
