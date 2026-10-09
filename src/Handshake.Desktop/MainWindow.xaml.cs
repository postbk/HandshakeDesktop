using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
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
        UpdateEventHeader(); LoadEventImages(); RefreshContacts(); StatusText.Text = $"Ready for {_event.Name}. Add a contact image or drag one into this window.";
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
        if (ImagesList.SelectedItem is not CaptureImage selectedImage) { StatusText.Text = "Select the image to extract before continuing."; return; }
        try
        {
            BusyIndicator.Visibility = System.Windows.Visibility.Visible; StatusText.Text = $"Extracting contacts from {selectedImage.DisplayName}…";
            var selected = new[] { selectedImage }; var extractor = new OpenAiExtractor(_settings); var result = await extractor.ExtractAsync(selected);
            using var document = System.Text.Json.JsonDocument.Parse(result); var root = document.RootElement; var contacts = root.TryGetProperty("contacts", out var list) ? list.EnumerateArray().ToList() : new[] { root }.ToList();
            var saved = 0;
            foreach (var item in contacts)
            {
                var name = JsonValue(item, "name"); var company = JsonValue(item, "company"); var website = JsonValue(item, "website"); var research = ""; var photoPath = selectedImage.Path;
                if (!string.IsNullOrWhiteSpace(company))
                {
                    StatusText.Text = $"Researching {company}…";
                    try { using var researchJson = System.Text.Json.JsonDocument.Parse(await extractor.ResearchAsync(company, website, selected, name)); var researchRoot = researchJson.RootElement; company = FirstValue(company, researchRoot, "company"); website = FirstValue(website, researchRoot, "website"); var address = JsonValue(item, "address"); var phone = JsonValue(item, "phone"); var email = JsonValue(item, "email"); address = FirstValue(address, researchRoot, "address"); phone = FirstValue(phone, researchRoot, "phone"); email = FirstValue(email, researchRoot, "email"); research = JsonValue(researchRoot, "summary"); var linkedIn = await FindLinkedInProfileAsync(name, company) ?? JsonValue(researchRoot, "linkedin_url"); var linkedInImage = await FindLinkedInImageAsync(linkedIn); var searchImages = await FindImageCandidatesAsync(name, company); var photoCandidates = new[] { linkedInImage, JsonValue(researchRoot, "profile_image_url") }.Concat(searchImages).Concat(JsonValues(researchRoot, "photo_urls")); foreach (var photoUrl in photoCandidates.Where(x => !string.IsNullOrWhiteSpace(x))) { if (!Uri.TryCreate(photoUrl, UriKind.Absolute, out _)) continue; try { photoPath = await DownloadPhotoAsync(photoUrl, name); break; } catch { } } SaveContact(item, name, company, website, address, phone, email, research, photoPath, selectedImage.Path, linkedIn); saved++; continue; } catch { }
                }
                SaveContact(item, name, company, website, JsonValue(item, "address"), JsonValue(item, "phone"), JsonValue(item, "email"), research, photoPath, selectedImage.Path, ""); saved++;
            }
            RefreshContacts(); StatusText.Text = $"Added {saved} contact(s) to the list. Use Edit to add details.";
        }
        catch (Exception error) { StatusText.Text = error.Message; }
        finally { BusyIndicator.Visibility = System.Windows.Visibility.Collapsed; }
    }

    private void SaveContact(System.Text.Json.JsonElement item, string name, string company, string website, string address, string phone, string email, string research, string photoPath, string cardImagePath, string linkedIn)
    { _store.SaveContact(new ContactRecord(Guid.NewGuid().ToString(), _event.Id, name, company, JsonValue(item, "title"), email, phone, website, address, item.GetRawText(), "", "", false, research, photoPath, cardImagePath, linkedIn, "")); }
    private async Task<string> DownloadPhotoAsync(string url, string personName)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) }; using var response = await client.GetAsync(url); response.EnsureSuccessStatusCode(); var bytes = await response.Content.ReadAsByteArrayAsync(); if (bytes.Length == 0 || bytes.Length > 10_000_000) throw new InvalidOperationException("Headshot was not a usable image."); var folder = Path.Combine(_store.GetEventFolder(_event.Id), "contact-photos"); Directory.CreateDirectory(folder); var path = Path.Combine(folder, $"{Guid.NewGuid():N}-{SanitizeFileName(personName)}.jpg"); await File.WriteAllBytesAsync(path, bytes);
        try { var image = new System.Windows.Media.Imaging.BitmapImage(); image.BeginInit(); image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; image.UriSource = new Uri(path); image.EndInit(); image.Freeze(); } catch { File.Delete(path); throw new InvalidOperationException("The search result was not a supported image format."); }
        return path;
    }
    private static string SanitizeFileName(string value) => string.Join("", value.Where(char.IsLetterOrDigit)).PadRight(1, 'p');
    private static async Task<string?> FindLinkedInProfileAsync(string person, string company)
    {
        if (string.IsNullOrWhiteSpace(person) || string.IsNullOrWhiteSpace(company)) return null;
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) }; client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 HandshakeDesktop/1.0");
        var query = Uri.EscapeDataString($"{person} {company} LinkedIn"); var html = await client.GetStringAsync($"https://www.bing.com/search?q={query}");
        var matches = System.Text.RegularExpressions.Regex.Matches(html, @"https?://(?:[a-z]{2,3}\.)?linkedin\.com/in/[A-Za-z0-9%_\-]+", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return matches.Cast<System.Text.RegularExpressions.Match>().Select(x => x.Value).Select(Uri.UnescapeDataString).FirstOrDefault(x => x.Contains("linkedin.com/in/", StringComparison.OrdinalIgnoreCase));
    }
    private static async Task<string?> FindLinkedInImageAsync(string? profileUrl)
    {
        if (string.IsNullOrWhiteSpace(profileUrl)) return null;
        try { using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) }; client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 HandshakeDesktop/1.0"); var html = await client.GetStringAsync(profileUrl); var match = System.Text.RegularExpressions.Regex.Match(html, @"<meta[^>]+property=[""']og:image[""'][^>]+content=[""']([^""']+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase); return match.Success ? System.Net.WebUtility.HtmlDecode(match.Groups[1].Value) : null; } catch { return null; }
    }
    private static async Task<IEnumerable<string>> FindImageCandidatesAsync(string person, string company)
    {
        try { using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) }; client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 HandshakeDesktop/1.0"); var query = Uri.EscapeDataString($"{person} {company}"); var html = await client.GetStringAsync($"https://www.bing.com/images/search?q={query}"); var matches = System.Text.RegularExpressions.Regex.Matches(html, @"[""']murl[""']\s*:\s*[""']([^""']+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase); return matches.Cast<System.Text.RegularExpressions.Match>().Select(x => System.Net.WebUtility.HtmlDecode(x.Groups[1].Value)).Distinct().Take(12).ToList(); } catch { return []; }
    }
    private static string JsonValue(System.Text.Json.JsonElement item, string property) => item.TryGetProperty(property, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() ?? "" : "";
    private static IEnumerable<string> JsonValues(System.Text.Json.JsonElement item, string property) => item.TryGetProperty(property, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.Array ? value.EnumerateArray().Where(x => x.ValueKind == System.Text.Json.JsonValueKind.String).Select(x => x.GetString() ?? "").Where(x => !string.IsNullOrWhiteSpace(x)) : [];
    private static string FirstValue(string current, System.Text.Json.JsonElement root, string property) => string.IsNullOrWhiteSpace(current) ? JsonValue(root, property) : current;
    private void RefreshContacts() { var query = SearchBox?.Text?.Trim() ?? ""; ContactsList.ItemsSource = _store.GetContacts(_event.Id).Where(c => string.IsNullOrWhiteSpace(query) || string.Join(" ", c.Name, c.Company, c.Title, c.Email, c.Phone, c.Website, c.Address).Contains(query, StringComparison.OrdinalIgnoreCase)).ToList(); }
    private void LoadEventImages() { _selectedImages.Clear(); foreach (var image in _store.GetCaptureImages(_event.Id)) _selectedImages.Add(image); StatusText.Text = _selectedImages.Count == 0 ? $"No images saved for {_event.Name}." : $"Loaded {_selectedImages.Count} saved image(s) for {_event.Name}."; }
    private async void DeleteImage_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (ImagesList.SelectedItem is not CaptureImage image) { StatusText.Text = "Select an image to delete."; return; }
        if (System.Windows.MessageBox.Show($"Delete {image.DisplayName}?", "Confirm image deletion", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning) != System.Windows.MessageBoxResult.Yes) return;
        _store.DeleteCaptureImage(_event.Id, image.Path);
        _selectedImages.Remove(image);
        ImagesList.ItemsSource = null;
        ImagesList.ItemsSource = _selectedImages;
        for (var attempt = 0; attempt < 10 && File.Exists(image.Path); attempt++)
        {
            try { File.Delete(image.Path); } catch (IOException) { await Task.Delay(100); }
        }
        StatusText.Text = File.Exists(image.Path) ? $"Removed {image.DisplayName} from the event, but Windows is still using the file." : $"Deleted {image.DisplayName}.";
    }
    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => RefreshContacts();
    private void ContactsList_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) { if (ContactsList.SelectedItem is ContactRecord contact) { var review = new ReviewWindow(_event, _store, contact.RawExtraction, contact, _selectedImages.ToList()) { Owner = this }; if (review.ShowDialog() == true) RefreshContacts(); } }
    private void EditSelectedContact_Click(object sender, System.Windows.RoutedEventArgs e) { if (ContactsList.SelectedItem is ContactRecord contact) { var review = new ReviewWindow(_event, _store, contact.RawExtraction, contact, _selectedImages.ToList()) { Owner = this }; if (review.ShowDialog() == true) RefreshContacts(); } }
    private void DeleteContact_Click(object sender, System.Windows.RoutedEventArgs e) { if (ContactsList.SelectedItem is not ContactRecord contact) return; if (System.Windows.MessageBox.Show($"Delete {contact.Name}?", "Confirm deletion", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning) != System.Windows.MessageBoxResult.Yes) return; _store.DeleteContact(contact.Id); RefreshContacts(); StatusText.Text = "Contact deleted."; }
    private void Window_Drop(object sender, System.Windows.DragEventArgs e) { if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) AddImages((string[])e.Data.GetData(System.Windows.DataFormats.FileDrop)!); }
    private void AddImages(IEnumerable<string> files) { foreach (var file in files.Where(IsImageFile).Take(Math.Max(0, 4 - _selectedImages.Count))) { try { var normalized = _normalizer.Normalize(file, _store.GetEventFolder(_event.Id)); var image = new CaptureImage(normalized, System.IO.Path.GetFileName(file)); _selectedImages.Add(image); _store.SaveCapture(_event, [image]); } catch (Exception error) { StatusText.Text = $"Could not import {System.IO.Path.GetFileName(file)}: {error.Message}"; } } StatusText.Text = _selectedImages.Count == 0 ? "No images selected yet." : $"{_selectedImages.Count} image(s) selected and saved for {_event.Name}."; }
    private static bool IsImageFile(string file) => new[] { ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".tif", ".tiff" }.Contains(System.IO.Path.GetExtension(file), StringComparer.OrdinalIgnoreCase);
    private void NewEvent_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var dialog = new System.Windows.Window { Title = "New category", Width = 420, Height = 260, WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner, Owner = this }; var name = new System.Windows.Controls.TextBox { Margin = new System.Windows.Thickness(16, 8, 16, 8) }; var location = new System.Windows.Controls.TextBox { Margin = new System.Windows.Thickness(16, 8, 16, 8) }; var dates = new System.Windows.Controls.TextBox { Margin = new System.Windows.Thickness(16, 8, 16, 8) }; var save = new System.Windows.Controls.Button { Content = "Create category", Padding = new System.Windows.Thickness(12, 7, 12, 7), Margin = new System.Windows.Thickness(16) }; var panel = new System.Windows.Controls.StackPanel(); panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Category name", Margin = new System.Windows.Thickness(16, 12, 16, 0) }); panel.Children.Add(name); panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Description", Margin = new System.Windows.Thickness(16, 0, 16, 0) }); panel.Children.Add(location); panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Notes", Margin = new System.Windows.Thickness(16, 0, 16, 0) }); panel.Children.Add(dates); panel.Children.Add(save); dialog.Content = panel; save.Click += (_, _) => { if (string.IsNullOrWhiteSpace(name.Text)) return; _event = _store.CreateEvent(name.Text, location.Text, dates.Text); EventPicker.ItemsSource = _store.GetEvents(); EventPicker.SelectedValuePath = "Id"; EventPicker.SelectedValue = _event.Id; UpdateEventHeader(); LoadEventImages(); RefreshContacts(); dialog.Close(); }; dialog.ShowDialog();
    }
    private void UpdateEventHeader() { EventNameText.Text = _event.Name; EventDetailsText.Text = string.Join(" · ", new[] { _event.Location, _event.Dates }.Where(x => !string.IsNullOrWhiteSpace(x))); }
    private void EventPicker_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) { if (EventPicker.SelectedItem is EventRecord selected && selected.Id != _event.Id) { _event = selected; UpdateEventHeader(); LoadEventImages(); RefreshContacts(); } }
    private void Settings_Click(object sender, System.Windows.RoutedEventArgs e) => ShowSettings();
    private void ShowSettings() { var dialog = new System.Windows.Window { Title = "Handshake settings", Width = 420, Height = 190, WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner, Owner = this }; var key = new System.Windows.Controls.PasswordBox { Margin = new System.Windows.Thickness(16), Password = _settings.HasApiKey ? "••••••••" : "" }; var save = new System.Windows.Controls.Button { Content = "Save encrypted key", Padding = new System.Windows.Thickness(12, 7, 12, 7), Margin = new System.Windows.Thickness(16, 0, 16, 16) }; save.Click += (_, _) => { if (key.Password != "••••••••" && !string.IsNullOrWhiteSpace(key.Password)) _settings.SaveApiKey(key.Password); dialog.Close(); }; var panel = new System.Windows.Controls.StackPanel(); panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "OpenAI API key (stored using Windows DPAPI)", Margin = new System.Windows.Thickness(16, 16, 16, 0) }); panel.Children.Add(key); panel.Children.Add(save); dialog.Content = panel; dialog.ShowDialog(); }
}
