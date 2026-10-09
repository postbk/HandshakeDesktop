using System.IO;
using System.Windows.Media.Imaging;

namespace Handshake.Desktop.Services;

public sealed class ImageNormalizer
{
    public string Normalize(string source, string folder)
    {
        Directory.CreateDirectory(folder); var destination = Path.Combine(folder, $"image-{Guid.NewGuid():N}.jpg");
        using var input = File.OpenRead(source); var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 2400; bitmap.StreamSource = input; bitmap.EndInit(); bitmap.Freeze();
        var encoder = new JpegBitmapEncoder { QualityLevel = 88 }; encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var output = File.Create(destination); encoder.Save(output); return destination;
    }

    public string Normalize(BitmapSource bitmap, string folder)
    {
        Directory.CreateDirectory(folder); var destination = Path.Combine(folder, $"clipboard-{Guid.NewGuid():N}.jpg");
        var encoder = new JpegBitmapEncoder { QualityLevel = 88 }; encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var output = File.Create(destination); encoder.Save(output); return destination;
    }

    public string CreateTightCrop(string source, string folder)
    {
        Directory.CreateDirectory(folder); var destination = Path.Combine(folder, $"card-crop-{Guid.NewGuid():N}.jpg");
        using var input = File.OpenRead(source); var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = input; bitmap.EndInit(); bitmap.Freeze();
        var marginX = (int)(bitmap.PixelWidth * 0.07); var marginY = (int)(bitmap.PixelHeight * 0.07);
        var crop = new CroppedBitmap(bitmap, new System.Windows.Int32Rect(marginX, marginY, Math.Max(1, bitmap.PixelWidth - marginX * 2), Math.Max(1, bitmap.PixelHeight - marginY * 2))); crop.Freeze();
        var encoder = new JpegBitmapEncoder { QualityLevel = 88 }; encoder.Frames.Add(BitmapFrame.Create(crop)); using var output = File.Create(destination); encoder.Save(output); return destination;
    }
}
