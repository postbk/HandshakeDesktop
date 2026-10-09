using Handshake.Desktop.Models;
using Handshake.Desktop.Services;
using System.IO;
using System.Net.Http;

namespace Handshake.Desktop;

public partial class ReviewWindow : System.Windows.Window
{
    private readonly EventRecord _event;
    private readonly LocalStore _store;
    private readonly ContactRecord? _existing;
    private readonly IReadOnlyList<CaptureImage> _images;
    private readonly AudioMemoService _audio = new();
    private System.Windows.Threading.DispatcherTimer? _memoTimer;
    private int _memoSeconds;
    private string _photoPath = "";
    private string _cardImagePath = "";
    public ReviewWindow(EventRecord eventRecord, LocalStore store, string raw, ContactRecord? existing = null, IEnumerable<CaptureImage>? images = null)
    {
        InitializeComponent(); _event = eventRecord; _store = store; _existing = existing; _images = (images ?? []).ToList(); RawBox.Text = raw;
        var text = raw.Replace("\\n", "\n");
        NameBox.Text = Find(text, "name"); CompanyBox.Text = Find(text, "company"); TitleBox.Text = Find(text, "title");
        EmailBox.Text = Find(text, "email"); PhoneBox.Text = Find(text, "phone"); WebsiteBox.Text = Find(text, "website"); AddressBox.Text = Find(text, "address");
        if (existing is not null) { NameBox.Text = existing.Name; CompanyBox.Text = existing.Company; TitleBox.Text = existing.Title; EmailBox.Text = existing.Email; PhoneBox.Text = existing.Phone; WebsiteBox.Text = existing.Website; LinkedInBox.Text = existing.LinkedInUrl; AddressBox.Text = existing.Address; BiographyBox.Text = existing.Biography; NotesBox.Text = existing.Notes; FollowUpBox.Text = existing.FollowUp; DoneBox.IsChecked = existing.Done; ResearchBox.Text = existing.Research; _photoPath = existing.PhotoPath; _cardImagePath = existing.CardImagePath; }
        UpdatePhotoPreview();
    }
    private static string Find(string text, string key)
    {
        var match = System.Text.RegularExpressions.Regex.Match(text, $"[\\\"']{key}[\\\"']\\s*:\\s*[\\\"']([^\\\"']*)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : "";
    }
    private static string JsonValue(System.Text.Json.JsonElement item, string property) => item.TryGetProperty(property, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() ?? "" : "";
    private void Save_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text) && string.IsNullOrWhiteSpace(CompanyBox.Text)) { System.Windows.MessageBox.Show("Enter a name or company."); return; }
        _store.SaveContact(new ContactRecord(_existing?.Id ?? Guid.NewGuid().ToString(), _event.Id, NameBox.Text.Trim(), CompanyBox.Text.Trim(), TitleBox.Text.Trim(), EmailBox.Text.Trim(), PhoneBox.Text.Trim(), WebsiteBox.Text.Trim(), AddressBox.Text.Trim(), RawBox.Text, NotesBox.Text.Trim(), FollowUpBox.Text.Trim(), DoneBox.IsChecked == true, ResearchBox.Text.Trim(), _photoPath, _cardImagePath, LinkedInBox.Text.Trim(), BiographyBox.Text.Trim()));
        DialogResult = true;
    }

    private async void Research_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CompanyBox.Text)) { System.Windows.MessageBox.Show("Enter a company first."); return; }
        try
        {
            ResearchBusyIndicator.Visibility = System.Windows.Visibility.Visible;
            var prompt = $"Research this exact company for a trade-show follow-up.\n\nContact name: {NameBox.Text}\nCompany as printed: {CompanyBox.Text}\nWebsite: {WebsiteBox.Text}\nEmail: {EmailBox.Text}\nPhone: {PhoneBox.Text}\nAddress: {AddressBox.Text}\nTitle: {TitleBox.Text}\n\nUse the attached business-card image to inspect logos, domains, email domains, printed claims, and other identifying details. Use those clues to disambiguate similarly named companies. Verify and return the company's website, address, phone, email, and a concise summary of what it does. Cite sources inline and do not include follow-up questions.";
            var promptBox = new System.Windows.Controls.TextBox { Text = prompt, AcceptsReturn = true, TextWrapping = System.Windows.TextWrapping.Wrap, VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto, Margin = new System.Windows.Thickness(16), Width = 600, Height = 260 };
            var run = new System.Windows.Controls.Button { Content = "Research", Padding = new System.Windows.Thickness(12, 7, 12, 7), HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new System.Windows.Thickness(16, 0, 16, 16) };
            var promptWindow = new System.Windows.Window { Title = "Review company research prompt", Width = 660, Height = 380, Owner = this, WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner };
            var promptPanel = new System.Windows.Controls.StackPanel(); promptPanel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Review or edit the research prompt before sending it.", Margin = new System.Windows.Thickness(16, 16, 16, 0) }); promptPanel.Children.Add(promptBox); promptPanel.Children.Add(run); promptWindow.Content = promptPanel;
            run.Click += (_, _) => promptWindow.DialogResult = true; if (promptWindow.ShowDialog() != true) return;
            var result = await new OpenAiExtractor(new SecureSettings()).ResearchAsync(CompanyBox.Text, WebsiteBox.Text, _images, NameBox.Text, promptBox.Text);
            using var json = System.Text.Json.JsonDocument.Parse(result); var root = json.RootElement;
            ResearchBox.Text = root.GetProperty("summary").GetString() ?? result;
            FillIfBlank(CompanyBox, root, "company"); FillIfBlank(WebsiteBox, root, "website"); FillIfBlank(AddressBox, root, "address"); FillIfBlank(PhoneBox, root, "phone"); FillIfBlank(EmailBox, root, "email");
        }
        catch (Exception error) { System.Windows.MessageBox.Show(error.Message, "Research failed"); } finally { ResearchBusyIndicator.Visibility = System.Windows.Visibility.Collapsed; }
    }

    private static void FillIfBlank(System.Windows.Controls.TextBox box, System.Text.Json.JsonElement root, string property)
    { if (string.IsNullOrWhiteSpace(box.Text) && root.TryGetProperty(property, out var value)) box.Text = value.GetString() ?? ""; }

    private void ReplacePhoto_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Choose contact photo", Filter = "Images|*.jpg;*.jpeg;*.png;*.webp;*.bmp|All files|*.*" };
        if (dialog.ShowDialog() != true) return;
        _photoPath = new ImageNormalizer().Normalize(dialog.FileName, Path.Combine(_store.GetEventFolder(_event.Id), "contact-photos")); UpdatePhotoPreview();
    }

    private void DeletePhoto_Click(object sender, System.Windows.RoutedEventArgs e) { if (File.Exists(_cardImagePath)) { try { _photoPath = new ImageNormalizer().CreateTightCrop(_cardImagePath, Path.Combine(_store.GetEventFolder(_event.Id), "contact-photos")); } catch { _photoPath = _cardImagePath; } } else _photoPath = ""; UpdatePhotoPreview(); }
    private void OpenLinkedIn_Click(object sender, System.Windows.RoutedEventArgs e) { if (Uri.TryCreate(LinkedInBox.Text.Trim(), UriKind.Absolute, out var uri)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
    private async void FindLinkedIn_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var browser = new LinkedInBrowserWindow(NameBox.Text.Trim(), CompanyBox.Text.Trim()) { Owner = this };
        if (browser.ShowDialog() != true || browser.Result is null) return;
        if (!string.IsNullOrWhiteSpace(browser.Result.Url) && browser.Result.Url.Contains("linkedin.com/in/", StringComparison.OrdinalIgnoreCase)) LinkedInBox.Text = browser.Result.Url;
        var pastedPhoto = PasteClipboardPhoto();
        var candidates = new[] { browser.Result.ImageUrl }.Concat(browser.Result.ImageUrls ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase);
        if (!pastedPhoto) foreach (var candidate in candidates) { try { _photoPath = await ImportBrowserPhotoAsync(candidate, NameBox.Text); UpdatePhotoPreview(); break; } catch { } }
        if (!string.IsNullOrWhiteSpace(browser.Result.VisibleText))
        {
            try { ResearchBusyIndicator.Visibility = System.Windows.Visibility.Visible; var summary = await new OpenAiExtractor(new SecureSettings()).SummarizeProfileAsync(browser.Result.VisibleText, NameBox.Text); using var json = System.Text.Json.JsonDocument.Parse(summary); var root = json.RootElement; var lines = new List<string>(); AddProfileLine(lines, "Role", root, "role"); AddProfileLine(lines, "What they do", root, "current_work"); AddProfileList(lines, "Former companies and roles", root, "former_companies_roles"); AddProfileLine(lines, "Highest degree", root, "highest_degree"); AddProfileList(lines, "Education", root, "education"); AddProfileLine(lines, "Biography", root, "biography"); BiographyBox.Text = string.Join("\n", lines); FillIfBlank(TitleBox, root, "role"); } catch { } finally { ResearchBusyIndicator.Visibility = System.Windows.Visibility.Collapsed; }
        }
    }
    private static void AddProfileLine(List<string> lines, string label, System.Text.Json.JsonElement root, string property) { var value = JsonValue(root, property); if (!string.IsNullOrWhiteSpace(value)) lines.Add($"{label}: {value}"); }
    private static void AddProfileList(List<string> lines, string label, System.Text.Json.JsonElement root, string property) { if (!root.TryGetProperty(property, out var values) || values.ValueKind != System.Text.Json.JsonValueKind.Array) return; var items = values.EnumerateArray().Where(x => x.ValueKind == System.Text.Json.JsonValueKind.String).Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList(); if (items.Count > 0) lines.Add($"{label}:\n" + string.Join("\n", items.Select(x => $"• {x}"))); }
    private void PastePhoto_Click(object sender, System.Windows.RoutedEventArgs e) { if (!PasteClipboardPhoto()) System.Windows.MessageBox.Show("Copy a profile image first, then click Paste copied photo."); }
    private bool PasteClipboardPhoto()
    {
        try { var bitmap = System.Windows.Clipboard.ContainsImage() ? System.Windows.Clipboard.GetImage() : null; if (bitmap is null) return false; _photoPath = new ImageNormalizer().Normalize(bitmap, Path.Combine(_store.GetEventFolder(_event.Id), "contact-photos")); UpdatePhotoPreview(); return true; } catch { return false; }
    }
    private async Task<string> ImportBrowserPhotoAsync(string url, string person)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) }; var bytes = await client.GetByteArrayAsync(url); if (bytes.Length == 0 || bytes.Length > 10_000_000) throw new InvalidOperationException("The profile image is not usable."); var folder = Path.Combine(_store.GetEventFolder(_event.Id), "contact-photos"); Directory.CreateDirectory(folder); var path = Path.Combine(folder, $"linkedin-{Guid.NewGuid():N}.jpg"); await File.WriteAllBytesAsync(path, bytes); try { var image = new System.Windows.Media.Imaging.BitmapImage(); image.BeginInit(); image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; image.UriSource = new Uri(path); image.EndInit(); image.Freeze(); return path; } catch { File.Delete(path); throw; }
    }
    private void UpdatePhotoPreview()
    {
        PhotoPreview.Source = null;
        if (!File.Exists(_photoPath)) return;
        try { var image = new System.Windows.Media.Imaging.BitmapImage(); image.BeginInit(); image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; image.UriSource = new Uri(_photoPath); image.EndInit(); image.Freeze(); PhotoPreview.Source = image; }
        catch { _photoPath = File.Exists(_cardImagePath) ? _cardImagePath : ""; if (!string.IsNullOrWhiteSpace(_photoPath)) { try { var fallback = new System.Windows.Media.Imaging.BitmapImage(); fallback.BeginInit(); fallback.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; fallback.UriSource = new Uri(_photoPath); fallback.EndInit(); fallback.Freeze(); PhotoPreview.Source = fallback; } catch { PhotoPreview.Source = null; } } }
    }

    private void RecordMemo_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        try { if (_audio.IsRecording) { _audio.Stop(); _memoTimer?.Stop(); RecordMemoButton.Content = "Record"; MemoCountdown.Text = ""; } else { _audio.Start(); _memoSeconds = 0; RecordMemoButton.Content = "Stop"; MemoCountdown.Text = "0:59"; _memoTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; _memoTimer.Tick += (_, _) => { _memoSeconds++; MemoCountdown.Text = $"0:{59 - _memoSeconds:00}"; if (_memoSeconds >= 59) RecordMemo_Click(this, new System.Windows.RoutedEventArgs()); }; _memoTimer.Start(); } } catch (Exception error) { System.Windows.MessageBox.Show(error.Message, "Recording failed"); }
    }

    private async void AnalyzeMemo_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_audio.CurrentPath) || !File.Exists(_audio.CurrentPath)) { System.Windows.MessageBox.Show("Record a memo first."); return; }
        try
        {
            MemoBusyIndicator.Visibility = System.Windows.Visibility.Visible;
            if (_audio.IsRecording) _audio.Stop();
            var transcript = await new OpenAiExtractor(new SecureSettings()).TranscribeAsync(_audio.CurrentPath!);
            var organized = await new OpenAiExtractor(new SecureSettings()).AnalyzeMemoAsync(transcript, NameBox.Text + " at " + CompanyBox.Text);
            using var json = System.Text.Json.JsonDocument.Parse(organized); var root = json.RootElement;
            var bullets = root.GetProperty("bullets").EnumerateArray().Select(x => "• " + x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)); var actions = root.GetProperty("follow_ups").EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x));
            NotesBox.Text = string.Join("\n", new[] { NotesBox.Text }.Concat(bullets).Where(x => !string.IsNullOrWhiteSpace(x))); FollowUpBox.Text = string.Join("\n", new[] { FollowUpBox.Text }.Concat(actions).Where(x => !string.IsNullOrWhiteSpace(x))); RecordMemoButton.Content = "Record";
        }
        catch (Exception error) { System.Windows.MessageBox.Show(error.Message, "Memo analysis failed"); } finally { MemoBusyIndicator.Visibility = System.Windows.Visibility.Collapsed; }
    }
}
