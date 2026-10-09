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
    private readonly ImageNormalizer _normalizer = new();
    private EventRecord _event = null!;

    public MainWindow()
    {
        InitializeComponent(); ImagesList.ItemsSource = _selectedImages;
        _event = _store.GetOrCreateDefaultEvent(); EventPicker.ItemsSource = _store.GetEvents(); EventPicker.SelectedValuePath = "Id"; EventPicker.SelectedValue = _event.Id;
        UpdateEventHeader(); LoadEventImages(); RefreshContacts(); StatusText.Text = $"Ready for {_event.Name}. Select images or drag them into this window.";
    }
    private void ChooseImages_Click(object sender, System.Windows.RoutedEventArgs e) { var d = new OpenFileDialog { Title = "Select business cards or company documents", Filter = "Images|*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.tif;*.tiff|All files|*.*", Multiselect = true }; if (d.ShowDialog() == true) AddImages(d.FileNames); }
    private void PasteImage_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        try { var data = System.Windows.Clipboard.GetDataObject(); var bitmap = data is not null && data.GetDataPresent(System.Windows.DataFormats.Bitmap) ? data.GetData(System.Windows.DataFormats.Bitmap) as System.Windows.Media.Imaging.BitmapSource : null; if (bitmap is null && System.Windows.Clipboard.ContainsImage()) bitmap = System.Windows.Clipboard.GetImage(); if (bitmap is null) { StatusText.Text = "No bitmap found. Copy an image to the clipboard, then paste again."; return; } if (_selectedImages.Count >= 4) { StatusText.Text = "A capture can contain up to four images."; return; } var normalized = _normalizer.Normalize(bitmap, _store.GetEventFolder(_event.Id)); var image = new CaptureImage(normalized, "Clipboard image"); _selectedImages.Add(image); _store.SaveCapture(_event, [image]); StatusText.Text = $"{_selectedImages.Count} image(s) selected and saved for {_event.Name}."; } catch (Exception error) { StatusText.Text = $"Could not paste image: {error.Message}"; }
    }
    private void Export_Click(object sender, System.Windows.RoutedEventArgs e) { var d = new SaveFileDialog { Title = "Export Handshake contacts", Filter = "CSV files|*.csv", FileName = "handshake-contacts.csv" }; if (d.ShowDialog() != true) return; _store.ExportCsv(_event.Id, d.FileName); StatusText.Text = "Contacts exported to CSV."; }
    private void ExportVcf_Click(object sender, System.Windows.RoutedEventArgs e) { var d = new SaveFileDialog { Title = "Export Handshake contacts", Filter = "vCard files|*.vcf", FileName = "handshake-contacts.vcf" }; if (d.ShowDialog() != true) return; _store.ExportVcf(_event.Id, d.FileName); StatusText.Text = "Contacts exported to vCard."; }
    private async void Extract_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_selectedImages.Count == 0) { StatusText.Text = "Select images before extraction."; return; } try { BusyIndicator.Visibility = System.Windows.Visibility.Visible; StatusText.Text = "Uploading images for extraction…"; var result = await new OpenAiExtractor(_settings).ExtractAsync(_selectedImages); var review = new ReviewWindow(_event, _store, result, images: _selectedImages.ToList()) { Owner = this }; if (review.ShowDialog() == true) { RefreshContacts(); StatusText.Text = "Contact reviewed and saved locally."; } else StatusText.Text = "Extraction completed; review was left open or cancelled."; } catch (Exception error) { StatusText.Text = error.Message; } finally { BusyIndicator.Visibility = System.Windows.Visibility.Collapsed; }
    }
    private void RefreshContacts() { var query = SearchBox?.Text?.Trim() ?? ""; ContactsList.ItemsSource = _store.GetContacts(_event.Id).Where(c => string.IsNullOrWhiteSpace(query) || string.Join(" ", c.Name, c.Company, c.Title, c.Email, c.Phone, c.Website, c.Address).Contains(query, StringComparison.OrdinalIgnoreCase)).ToList(); }
    private void LoadEventImages() { _selectedImages.Clear(); foreach (var image in _store.GetCaptureImages(_event.Id)) _selectedImages.Add(image); StatusText.Text = _selectedImages.Count == 0 ? $"No images saved for {_event.Name}." : $"Loaded {_selectedImages.Count} saved image(s) for {_event.Name}."; }
    private void DeleteImage_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (ImagesList.SelectedItem is not CaptureImage image) { StatusText.Text = "Select an image to delete."; return; }
        if (System.Windows.MessageBox.Show($"Delete {image.DisplayName}?", "Confirm image deletion", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning) != System.Windows.MessageBoxResult.Yes) return;
        _store.DeleteCaptureImage(_event.Id, image.Path); if (File.Exists(image.Path)) File.Delete(image.Path); _selectedImages.Remove(image); StatusText.Text = $"Deleted {image.DisplayName}.";
    }
    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => RefreshContacts();
    private void ContactsList_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) { if (ContactsList.SelectedItem is ContactRecord contact) { var review = new ReviewWindow(_event, _store, contact.RawExtraction, contact, _selectedImages.ToList()) { Owner = this }; if (review.ShowDialog() == true) RefreshContacts(); } }
    private void DeleteContact_Click(object sender, System.Windows.RoutedEventArgs e) { if (ContactsList.SelectedItem is not ContactRecord contact) return; if (System.Windows.MessageBox.Show($"Delete {contact.Name}?", "Confirm deletion", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning) != System.Windows.MessageBoxResult.Yes) return; _store.DeleteContact(contact.Id); RefreshContacts(); StatusText.Text = "Contact deleted."; }
    private void Window_Drop(object sender, System.Windows.DragEventArgs e) { if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) AddImages((string[])e.Data.GetData(System.Windows.DataFormats.FileDrop)!); }
    private void AddImages(IEnumerable<string> files) { foreach (var file in files.Where(IsImageFile).Take(Math.Max(0, 4 - _selectedImages.Count))) { try { var normalized = _normalizer.Normalize(file, _store.GetEventFolder(_event.Id)); var image = new CaptureImage(normalized, System.IO.Path.GetFileName(file)); _selectedImages.Add(image); _store.SaveCapture(_event, [image]); } catch (Exception error) { StatusText.Text = $"Could not import {System.IO.Path.GetFileName(file)}: {error.Message}"; } } StatusText.Text = _selectedImages.Count == 0 ? "No images selected yet." : $"{_selectedImages.Count} image(s) selected and saved for {_event.Name}."; }
    private static bool IsImageFile(string file) => new[] { ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".tif", ".tiff" }.Contains(System.IO.Path.GetExtension(file), StringComparer.OrdinalIgnoreCase);
    private void NewEvent_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var dialog = new System.Windows.Window { Title = "New event", Width = 420, Height = 260, WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner, Owner = this }; var name = new System.Windows.Controls.TextBox { Margin = new System.Windows.Thickness(16, 8, 16, 8) }; var location = new System.Windows.Controls.TextBox { Margin = new System.Windows.Thickness(16, 8, 16, 8) }; var dates = new System.Windows.Controls.TextBox { Margin = new System.Windows.Thickness(16, 8, 16, 8) }; var save = new System.Windows.Controls.Button { Content = "Create event", Padding = new System.Windows.Thickness(12, 7, 12, 7), Margin = new System.Windows.Thickness(16) }; var panel = new System.Windows.Controls.StackPanel(); panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Event name", Margin = new System.Windows.Thickness(16, 12, 16, 0) }); panel.Children.Add(name); panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Location", Margin = new System.Windows.Thickness(16, 0, 16, 0) }); panel.Children.Add(location); panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Dates", Margin = new System.Windows.Thickness(16, 0, 16, 0) }); panel.Children.Add(dates); panel.Children.Add(save); dialog.Content = panel; save.Click += (_, _) => { if (string.IsNullOrWhiteSpace(name.Text)) return; _event = _store.CreateEvent(name.Text, location.Text, dates.Text); EventPicker.ItemsSource = _store.GetEvents(); EventPicker.SelectedValuePath = "Id"; EventPicker.SelectedValue = _event.Id; UpdateEventHeader(); LoadEventImages(); RefreshContacts(); dialog.Close(); }; dialog.ShowDialog();
    }
    private void UpdateEventHeader() { EventNameText.Text = _event.Name; EventDetailsText.Text = string.Join(" · ", new[] { _event.Location, _event.Dates }.Where(x => !string.IsNullOrWhiteSpace(x))); }
    private void EventPicker_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) { if (EventPicker.SelectedItem is EventRecord selected && selected.Id != _event.Id) { _event = selected; UpdateEventHeader(); LoadEventImages(); RefreshContacts(); } }
    private void Settings_Click(object sender, System.Windows.RoutedEventArgs e) => ShowSettings();
    private void ShowSettings() { var dialog = new System.Windows.Window { Title = "Handshake settings", Width = 420, Height = 190, WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner, Owner = this }; var key = new System.Windows.Controls.PasswordBox { Margin = new System.Windows.Thickness(16), Password = _settings.HasApiKey ? "••••••••" : "" }; var save = new System.Windows.Controls.Button { Content = "Save encrypted key", Padding = new System.Windows.Thickness(12, 7, 12, 7), Margin = new System.Windows.Thickness(16, 0, 16, 16) }; save.Click += (_, _) => { if (key.Password != "••••••••" && !string.IsNullOrWhiteSpace(key.Password)) _settings.SaveApiKey(key.Password); dialog.Close(); }; var panel = new System.Windows.Controls.StackPanel(); panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "OpenAI API key (stored using Windows DPAPI)", Margin = new System.Windows.Thickness(16, 16, 16, 0) }); panel.Children.Add(key); panel.Children.Add(save); dialog.Content = panel; dialog.ShowDialog(); }
}
