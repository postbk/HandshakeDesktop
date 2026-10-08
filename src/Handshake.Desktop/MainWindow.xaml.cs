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
    private readonly AudioMemoService _audio = new();
    private System.Windows.Threading.DispatcherTimer? _memoTimer;
    private int _memoSeconds;
    private EventRecord _event = null!;

    public MainWindow()
    {
        InitializeComponent();
        ImagesList.ItemsSource = _selectedImages;
        _event = _store.GetOrCreateDefaultEvent();
        UpdateEventHeader();
        RefreshContacts();
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

    private void Export_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Title = "Export Handshake contacts", Filter = "CSV files|*.csv", FileName = "handshake-contacts.csv" };
        if (dialog.ShowDialog() != true) return;
        _store.ExportCsv(_event.Id, dialog.FileName);
        StatusText.Text = "Contacts exported to CSV.";
    }

    private void ExportVcf_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Title = "Export Handshake contacts", Filter = "vCard files|*.vcf", FileName = "handshake-contacts.vcf" };
        if (dialog.ShowDialog() != true) return;
        _store.ExportVcf(_event.Id, dialog.FileName);
        StatusText.Text = "Contacts exported to vCard.";
    }

    private void Memo_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        try
        {
            if (!_audio.IsRecording)
            {
                _audio.Start(); _memoSeconds = 0; MemoButton.Content = "Stop voice memo (0:59)"; StatusText.Text = "Recording private voice memo…";
                _memoTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; _memoTimer.Tick += (_, _) => { _memoSeconds++; MemoButton.Content = $"Stop voice memo (0:{59 - _memoSeconds:00})"; if (_memoSeconds >= 59) Memo_Click(this, new System.Windows.RoutedEventArgs()); }; _memoTimer.Start();
            }
            else
            {
                var path = _audio.Stop(); _memoTimer?.Stop(); MemoButton.Content = "Record voice memo (59 sec max)"; StatusText.Text = path is null ? "No memo recorded." : $"Saved private voice memo: {Path.GetFileName(path)}";
            }
        }
        catch (Exception error) { StatusText.Text = $"Microphone unavailable: {error.Message}"; }
    }

    private async void Extract_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_selectedImages.Count == 0) { StatusText.Text = "Select images before extraction."; return; }
        try
        {
            StatusText.Text = "Uploading images for extraction…";
            var result = await new OpenAiExtractor(_settings).ExtractAsync(_selectedImages);
            var review = new ReviewWindow(_event, _store, result) { Owner = this };
            if (review.ShowDialog() == true) { RefreshContacts(); StatusText.Text = "Contact reviewed and saved locally."; }
            else StatusText.Text = "Extraction completed; review was left open or cancelled.";
        }
        catch (Exception error) { StatusText.Text = error.Message; }
    }

    private void RefreshContacts()
    {
        var query = SearchBox?.Text?.Trim() ?? "";
        ContactsList.ItemsSource = _store.GetContacts(_event.Id).Where(c => string.IsNullOrWhiteSpace(query) ||
            string.Join(" ", c.Name, c.Company, c.Title, c.Email, c.Phone, c.Website, c.Address).Contains(query, StringComparison.OrdinalIgnoreCase)).Select(c =>
            string.IsNullOrWhiteSpace(c.Company) ? c.Name : $"{c.Name} — {c.Company}").ToList();
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => RefreshContacts();

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

    private void NewEvent_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var dialog = new System.Windows.Window { Title = "New event", Width = 420, Height = 260, WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner, Owner = this };
        var name = new System.Windows.Controls.TextBox { Margin = new System.Windows.Thickness(16, 8, 16, 8) }; var location = new System.Windows.Controls.TextBox { Margin = new System.Windows.Thickness(16, 8, 16, 8) }; var dates = new System.Windows.Controls.TextBox { Margin = new System.Windows.Thickness(16, 8, 16, 8) }; var save = new System.Windows.Controls.Button { Content = "Create event", Padding = new System.Windows.Thickness(12, 7, 12, 7), Margin = new System.Windows.Thickness(16) };
        var panel = new System.Windows.Controls.StackPanel(); panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Event name", Margin = new System.Windows.Thickness(16, 12, 16, 0) }); panel.Children.Add(name); panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Location", Margin = new System.Windows.Thickness(16, 0, 16, 0) }); panel.Children.Add(location); panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Dates", Margin = new System.Windows.Thickness(16, 0, 16, 0) }); panel.Children.Add(dates); panel.Children.Add(save); dialog.Content = panel;
        save.Click += (_, _) => { if (string.IsNullOrWhiteSpace(name.Text)) return; _event = _store.CreateEvent(name.Text, location.Text, dates.Text); UpdateEventHeader(); RefreshContacts(); dialog.Close(); };
        dialog.ShowDialog();
    }

    private void UpdateEventHeader() { EventNameText.Text = _event.Name; EventDetailsText.Text = string.Join(" · ", new[] { _event.Location, _event.Dates }.Where(x => !string.IsNullOrWhiteSpace(x))); }

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
