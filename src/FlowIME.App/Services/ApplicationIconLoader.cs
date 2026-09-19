using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace FlowIME.App.Services;

public sealed class ApplicationIconLoader
{
    public async ValueTask<ImageSource?> LoadAsync(
        string executablePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return null;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = await StorageFile.GetFileFromPathAsync(executablePath);
            cancellationToken.ThrowIfCancellationRequested();

            using var thumbnail = await file.GetThumbnailAsync(
                ThumbnailMode.SingleItem,
                32,
                ThumbnailOptions.UseCurrentScale);
            if (thumbnail is null || thumbnail.Size == 0)
            {
                return null;
            }

            var image = new BitmapImage();
            await image.SetSourceAsync(thumbnail);
            return image;
        }
        catch (Exception ex) when (
            ex is UnauthorizedAccessException or FileNotFoundException or ArgumentException or COMException)
        {
            return null;
        }
    }
}
