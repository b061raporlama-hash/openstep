using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenSteps.App;

namespace OpenSteps.Tests;

public sealed class FileImageConverterTests
{
    [Fact]
    public void ThumbnailDecode_BoundsDimensionsWithoutChangingSourceFile()
    {
        OnSta(() =>
        {
            var path = CreateScreenshot();
            try
            {
                var originalBytes = File.ReadAllBytes(path);
                var image = Assert.IsType<BitmapImage>(new FileImageConverter().Convert(
                    path, typeof(BitmapSource), "640", CultureInfo.InvariantCulture));

                Assert.Equal(640, image.PixelWidth);
                Assert.Equal(360, image.PixelHeight);
                Assert.True(image.IsFrozen);
                Assert.Equal(originalBytes, File.ReadAllBytes(path));
                // OnLoad must release the source file, including for reduced images.
                using var file = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void DecodeWithoutParameter_RetainsFullResolution()
    {
        OnSta(() =>
        {
            var path = CreateScreenshot();
            try
            {
                var image = Assert.IsType<BitmapImage>(new FileImageConverter().Convert(
                    path, typeof(BitmapSource), null!, CultureInfo.InvariantCulture));

                Assert.Equal(3200, image.PixelWidth);
                Assert.Equal(1800, image.PixelHeight);
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    private static string CreateScreenshot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"OpenSteps-thumbnail-{Guid.NewGuid():N}.png");
        var bitmap = BitmapSource.Create(3200, 1800, 96, 96, PixelFormats.Bgra32,
            null, new byte[3200 * 1800 * 4], 3200 * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
        return path;
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Image conversion test timed out.");
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
