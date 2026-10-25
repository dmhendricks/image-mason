using ImageMason.Configuration;
using ImageMason.Imaging;
using ImageMason.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace ImageMason.Tests.Storage;

public sealed class ImageStoreTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("image-mason-tests-");

    public void Dispose() => _directory.Delete(recursive: true);

    private ImageStore CreateStore(long maxStoreBytes = 0) =>
        new(new ImageMasonOptions { StorePath = _directory.FullName, ImageTtlMinutes = 60, MaxStoreBytes = maxStoreBytes },
            NullLogger<ImageStore>.Instance);

    [Theory]
    [InlineData("photo.jpg", true)]
    [InlineData("my_photo-2.png", true)]
    [InlineData("0123456789abcdef0123456789abcdef.jpg", true)]
    [InlineData("a.jpg", true)]
    [InlineData("banner.webp", true)]
    [InlineData("banner.avif", true)]
    [InlineData("photo.jpeg", false)]
    [InlineData(".jpg", false)]
    [InlineData("photo.gif", false)]
    [InlineData("photo.JPG", false)]
    [InlineData("photo.min.jpg", false)]
    [InlineData("../photo.jpg", false)]
    [InlineData("dir/photo.jpg", false)]
    [InlineData("photo .jpg", false)]
    public void IsStoredName_AllowsOnlySafeSingleSegmentNames(string name, bool expected)
    {
        Assert.Equal(expected, ImageStore.IsStoredName(name));
    }

    [Fact]
    public async Task Save_ThenTryGet_ReturnsTheFile()
    {
        var store = CreateStore();
        var data = TestImages.NoisyPng(8, 8);

        var saved = await store.SaveAsync(data, ImageFormat.Png, null, TestContext.Current.CancellationToken);

        Assert.Matches("^[0-9a-f]{32}\\.png$", saved.Name);
        Assert.True(store.TryGet(saved.Name, out var file));
        Assert.Equal("image/png", file.ContentType);
        Assert.Equal(data, await File.ReadAllBytesAsync(file.Path, TestContext.Current.CancellationToken));
        Assert.InRange(file.MaxAgeSeconds, 3590, 3600);
    }

    [Fact]
    public async Task Save_WithTheSameName_ReplacesTheFile()
    {
        var store = CreateStore();

        await store.SaveAsync([1, 2, 3], ImageFormat.Jpeg, "photo.jpg", TestContext.Current.CancellationToken);
        await store.SaveAsync([4, 5], ImageFormat.Jpeg, "photo.jpg", TestContext.Current.CancellationToken);

        Assert.True(store.TryGet("photo.jpg", out var file));
        Assert.Equal([4, 5], await File.ReadAllBytesAsync(file.Path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Save_OverTheCap_DeletesTheOldestFiles()
    {
        var store = CreateStore(maxStoreBytes: 250);
        await store.SaveAsync(new byte[100], ImageFormat.Png, "first.png", TestContext.Current.CancellationToken);
        File.SetLastWriteTimeUtc(Path.Combine(_directory.FullName, "first.png"), DateTime.UtcNow.AddMinutes(-2));
        await store.SaveAsync(new byte[100], ImageFormat.Png, "second.png", TestContext.Current.CancellationToken);
        File.SetLastWriteTimeUtc(Path.Combine(_directory.FullName, "second.png"), DateTime.UtcNow.AddMinutes(-1));

        await store.SaveAsync(new byte[100], ImageFormat.Png, "third.png", TestContext.Current.CancellationToken);

        Assert.False(store.TryGet("first.png", out _));
        Assert.True(store.TryGet("second.png", out _));
        Assert.True(store.TryGet("third.png", out _));
    }

    [Fact]
    public async Task TryGet_DoesNotServeAnExpiredFile_AndDeletesIt()
    {
        var store = CreateStore();
        await store.SaveAsync([1], ImageFormat.Jpeg, "old.jpg", TestContext.Current.CancellationToken);
        var path = Path.Combine(_directory.FullName, "old.jpg");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-2));

        Assert.False(store.TryGet("old.jpg", out _));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task DeleteExpired_RemovesOnlyExpiredFiles()
    {
        var store = CreateStore();
        await store.SaveAsync([1], ImageFormat.Jpeg, "old.jpg", TestContext.Current.CancellationToken);
        await store.SaveAsync([1], ImageFormat.Jpeg, "new.jpg", TestContext.Current.CancellationToken);
        File.SetLastWriteTimeUtc(Path.Combine(_directory.FullName, "old.jpg"), DateTime.UtcNow.AddHours(-2));

        Assert.Equal(1, store.DeleteExpired());
        Assert.False(File.Exists(Path.Combine(_directory.FullName, "old.jpg")));
        Assert.True(store.TryGet("new.jpg", out _));
    }

    [Fact]
    public async Task Delete_RemovesTheFile()
    {
        var store = CreateStore();
        await store.SaveAsync([1], ImageFormat.WebP, "banner.webp", TestContext.Current.CancellationToken);

        Assert.True(store.Delete("banner.webp"));
        Assert.False(File.Exists(Path.Combine(_directory.FullName, "banner.webp")));
        Assert.False(store.Delete("banner.webp"));
    }

    [Fact]
    public async Task Delete_ReportsAnExpiredFileAsMissing_AndRemovesIt()
    {
        var store = CreateStore();
        await store.SaveAsync([1], ImageFormat.Jpeg, "old.jpg", TestContext.Current.CancellationToken);
        var path = Path.Combine(_directory.FullName, "old.jpg");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-2));

        Assert.False(store.Delete("old.jpg"));
        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData("../secret.jpg")]
    [InlineData("notes.txt")]
    [InlineData("missing.png")]
    public void Delete_RefusesUnsafeOrUnknownNames(string name)
    {
        var outside = Path.Combine(_directory.Parent!.FullName, "secret.jpg");
        File.WriteAllBytes(outside, [1]);
        try
        {
            Assert.False(CreateStore().Delete(name));
            Assert.True(File.Exists(outside));
        }
        finally
        {
            File.Delete(outside);
        }
    }
}
