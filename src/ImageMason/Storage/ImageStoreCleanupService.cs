namespace ImageMason.Storage;

/// <summary>Deletes stored images older than IMAGE_MASON_IMAGE_TTL_MINUTES. Runs once at startup, then every minute.</summary>
public sealed class ImageStoreCleanupService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly ImageStore _store;
    private readonly ILogger<ImageStoreCleanupService> _logger;

    public ImageStoreCleanupService(ImageStore store, ILogger<ImageStoreCleanupService> logger)
    {
        _store = store;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                var deleted = _store.DeleteExpired();
                if (deleted > 0)
                    _logger.LogInformation("Deleted {Count} expired stored image(s)", deleted);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Stored image cleanup failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
