using ImageMason.Imaging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ImageMason.Tests.Imaging;

public sealed class QualitySearchTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // The defaults for jpegli's distance (higher is smaller) and cwebp's quality (lower is smaller).
    private static readonly SearchRange Distance = new(Start: 1.4, Min: 0.5, Max: 6.0, Step: 0.5, Precision: 0.25, Smaller: 1, Integer: false);
    private static readonly SearchRange Quality = new(Start: 89, Min: 10, Max: 100, Step: 5, Precision: 1, Smaller: -1, Integer: true);

    /// <summary>Runs a search against a fake encoder whose files get smaller in the range's smaller direction.</summary>
    private static async Task<(Candidate? Found, double[] Tried)> SearchAsync(SearchRange range, double minScore,
        Func<double, double?> score, int maxSteps = 5)
    {
        var tried = new List<double>();
        var search = new QualitySearch(maxSteps, NullLogger.Instance);
        var found = await search.SearchAsync("test", range, minScore, (value, suffix) =>
        {
            Assert.Equal(tried.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), suffix);
            tried.Add(value);
            return Task.FromResult<Candidate?>(new($"{value:0.###}", suffix, (long)(10_000 - range.Smaller * value * 100), score(value)));
        }, Token);

        return (found, [.. tried.Select(v => Math.Round(v, 3))]);
    }

    [Fact]
    public async Task BracketsTheFloor_AndReturnsTheSmallestPassingResult()
    {
        var (found, tried) = await SearchAsync(Distance, minScore: 85, d => 100 - 10 * d);

        Assert.Equal([1.4, 1.9, 1.5, 1.54], tried);
        Assert.Equal("1.5", found?.Method);
    }

    [Fact]
    public async Task IntegerRange_TriesOnlyWholeValues_AndFindsTheLowestPassingQuality()
    {
        var (found, tried) = await SearchAsync(Quality, minScore: 80, q => q);

        Assert.Equal([89, 84, 74, 80, 79], tried);
        Assert.Equal("80", found?.Method);
    }

    [Fact]
    public async Task WhenNothingPasses_EndsAtTheGentlestSetting()
    {
        // The JPEG encoder keeps that last attempt as its fallback rather than encoding it again.
        var (found, tried) = await SearchAsync(Distance, minScore: 85, _ => 50);

        Assert.Null(found);
        Assert.Equal(Distance.Min, tried[^1]);
    }

    [Fact]
    public async Task WhenEverythingPasses_DoublesTheStepUntilTheLimit()
    {
        var (found, tried) = await SearchAsync(Quality, minScore: 80, _ => 95);

        Assert.Equal([89, 84, 74, 54, 14], tried);
        Assert.Equal("14", found?.Method);
    }

    [Fact]
    public async Task StopsAtTheEdgeOfTheRange()
    {
        var (found, tried) = await SearchAsync(Distance with { Start = 5 }, minScore: 50, _ => 95, maxSteps: 10);

        Assert.Equal([5, 5.5, 6], tried);
        Assert.Equal("6", found?.Method);
    }

    [Fact]
    public async Task AnUnscoredResultFails()
    {
        var (found, tried) = await SearchAsync(Quality, minScore: 80, q => q >= 89 ? null : 95);

        Assert.Equal([89, 94, 100], tried);
        Assert.Null(found);
    }

    [Fact]
    public async Task AFailedEncodeEndsTheSearch_KeepingTheBestSoFar()
    {
        var calls = 0;
        var search = new QualitySearch(5, NullLogger.Instance);
        var found = await search.SearchAsync("test", Quality, 80,
            (q, suffix) => Task.FromResult(++calls == 1 ? new Candidate("first", suffix, 100, 90) : null), Token);

        Assert.Equal(2, calls);
        Assert.Equal("first", found?.Method);
    }

    [Fact]
    public async Task Cancelled_Throws()
    {
        using var cts = new CancellationTokenSource();
        var search = new QualitySearch(5, NullLogger.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => search.SearchAsync("test", Quality, 80, (q, suffix) =>
        {
            cts.Cancel();
            return Task.FromResult<Candidate?>(new("q", suffix, 100, 90));
        }, cts.Token));
    }
}
