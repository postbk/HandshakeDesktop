using System.IO;
using System.Windows.Media.Imaging;

namespace Handshake.Desktop.Services;

public sealed class ImageNormalizer
{
    private readonly string _folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Handshake", "images");
    public string Normalize(string source)
    {
        Directory.CreateDirectory(_folder); var destination = Path.Combine(_folder, $"image-{Guid.NewGuid():N}.jpg");
        using var input = File.OpenRead(source); var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 2400; bitmap.StreamSource = input; bitmap.EndInit(); bitmap.Freeze();
        var encoder = new JpegBitmapEncoder { QualityLevel = 88 }; encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var output = File.Create(destination); encoder.Save(output); return destination;
    }

    public string Normalize(BitmapSource bitmap)
    {
        Directory.CreateDirectory(_folder); var destination = Path.Combine(_folder, $"clipboard-{Guid.NewGuid():N}.jpg");
        var encoder = new JpegBitmapEncoder { QualityLevel = 88 }; encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var output = File.Create(destination); encoder.Save(output); return destination;
    }
}
