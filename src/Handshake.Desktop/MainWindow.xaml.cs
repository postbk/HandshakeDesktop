using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using Handshake.Desktop.Models;
using Handshake.Desktop.Services;

namespace Handshake.Desktop;

public partial class MainWindow : System.Windows.Window
{
    private readonly ObservableCollection<CaptureImage> _selectedImages = [];
    private readonly LocalStore _store = new();
    private readonly SecureSettings _settings = new();
    private EventRecord _event = null!;

    public MainWindow()
    {
        InitializeComponent();
        ImagesList.ItemsSource = _selectedImages;
        _event = _store.GetOrCreateDefaultEvent();
        StatusText.Text = $"Ready for {_event.Name}. Select images or drag them into this window.";
    }

    private void ChooseImages_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select business cards or company documents",
            Filter = "Images|*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.tif;*.tiff|All files|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog() != true) return;
        AddImages(dialog.FileNames);
    }

    private void GooglePhotos_Click(object sender, System.Windows.RoutedEventArgs e) =>
        System.Windows.MessageBox.Show("Google Photos Picker will be connected in the next increment. It will use explicit user-selected photos rather than broad library access.", "Google Photos");

    private void SaveCapture_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_selectedImages.Count == 0)
        {
            StatusText.Text = "Select at least one image before saving a capture.";
            return;
        }
        _store.SaveCapture(_event, _selectedImages);
        StatusText.Text = $"Saved {_selectedImages.Count} image(s) locally for {_event.Name}.";
    }

    private async void Extract_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_selectedImages.Count == 0) { StatusText.Text = "Select images before extraction."; return; }
        try
        {
            StatusText.Text = "Uploading images for extraction…";
            var result = await new OpenAiExtractor(_settings).ExtractAsync(_selectedImages);
            var review = new ReviewWindow(_event, _store, result) { Owner = this };
            if (review.ShowDialog() == true) StatusText.Text = "Contact reviewed and saved locally.";
            else StatusText.Text = "Extraction completed; review was left open or cancelled.";
        }
        catch (Exception error) { StatusText.Text = error.Message; }
    }

    private void Window_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return;
        AddImages((string[])e.Data.GetData(System.Windows.DataFormats.FileDrop)!);
    }

    private void AddImages(IEnumerable<string> files)
    {
        var imageFiles = files.Where(IsImageFile).Take(Math.Max(0, 4 - _selectedImages.Count));
        foreach (var file in imageFiles)
            if (_selectedImages.All(x => !string.Equals(x.Path, file, StringComparison.OrdinalIgnoreCase)))
                _selectedImages.Add(new CaptureImage(file, Path.GetFileName(file)));
        StatusText.Text = _selectedImages.Count == 0
            ? "No images selected yet."
            : $"{_selectedImages.Count} image(s) selected. Review them before extraction.";
    }

    private static bool IsImageFile(string file) =>
        new[] { ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".tif", ".tiff" }
            .Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase);

    private void NewEvent_Click(object sender, System.Windows.RoutedEventArgs e) =>
        System.Windows.MessageBox.Show("Event creation is the next database-backed increment.", "New event");

    private void Settings_Click(object sender, System.Windows.RoutedEventArgs e) =>
        ShowSettings();

    private void ShowSettings()
    {
        var dialog = new System.Windows.Window { Title = "Handshake settings", Width = 420, Height = 190, WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner, Owner = this };
        var key = new System.Windows.Controls.PasswordBox { Margin = new System.Windows.Thickness(16), Password = _settings.HasApiKey ? "••••••••" : "" };
        var save = new System.Windows.Controls.Button { Content = "Save encrypted key", Padding = new System.Windows.Thickness(12, 7, 12, 7), Margin = new System.Windows.Thickness(16, 0, 16, 16) };
        save.Click += (_, _) => { if (key.Password != "••••••••" && !string.IsNullOrWhiteSpace(key.Password)) _settings.SaveApiKey(key.Password); dialog.Close(); };
        var panel = new System.Windows.Controls.StackPanel(); panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "OpenAI API key (stored using Windows DPAPI)", Margin = new System.Windows.Thickness(16, 16, 16, 0) }); panel.Children.Add(key); panel.Children.Add(save); dialog.Content = panel; dialog.ShowDialog();
    }
}
