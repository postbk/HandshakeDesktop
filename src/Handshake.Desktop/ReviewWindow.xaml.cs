using Handshake.Desktop.Models;
using Handshake.Desktop.Services;
using System.IO;

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
    public ReviewWindow(EventRecord eventRecord, LocalStore store, string raw, ContactRecord? existing = null, IEnumerable<CaptureImage>? images = null)
    {
        InitializeComponent(); _event = eventRecord; _store = store; _existing = existing; _images = (images ?? []).ToList(); RawBox.Text = raw;
        var text = raw.Replace("\\n", "\n");
        NameBox.Text = Find(text, "name"); CompanyBox.Text = Find(text, "company"); TitleBox.Text = Find(text, "title");
        EmailBox.Text = Find(text, "email"); PhoneBox.Text = Find(text, "phone"); WebsiteBox.Text = Find(text, "website"); AddressBox.Text = Find(text, "address");
        if (existing is not null) { NameBox.Text = existing.Name; CompanyBox.Text = existing.Company; TitleBox.Text = existing.Title; EmailBox.Text = existing.Email; PhoneBox.Text = existing.Phone; WebsiteBox.Text = existing.Website; AddressBox.Text = existing.Address; NotesBox.Text = existing.Notes; FollowUpBox.Text = existing.FollowUp; DoneBox.IsChecked = existing.Done; ResearchBox.Text = existing.Research; }
    }
    private static string Find(string text, string key)
    {
        var match = System.Text.RegularExpressions.Regex.Match(text, $"[\\\"']{key}[\\\"']\\s*:\\s*[\\\"']([^\\\"']*)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : "";
    }
    private void Save_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text) && string.IsNullOrWhiteSpace(CompanyBox.Text)) { System.Windows.MessageBox.Show("Enter a name or company."); return; }
        _store.SaveContact(new ContactRecord(_existing?.Id ?? Guid.NewGuid().ToString(), _event.Id, NameBox.Text.Trim(), CompanyBox.Text.Trim(), TitleBox.Text.Trim(), EmailBox.Text.Trim(), PhoneBox.Text.Trim(), WebsiteBox.Text.Trim(), AddressBox.Text.Trim(), RawBox.Text, NotesBox.Text.Trim(), FollowUpBox.Text.Trim(), DoneBox.IsChecked == true, ResearchBox.Text.Trim()));
        DialogResult = true;
    }

    private async void Research_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CompanyBox.Text)) { System.Windows.MessageBox.Show("Enter a company first."); return; }
        try
        {
            ResearchBusyIndicator.Visibility = System.Windows.Visibility.Visible;
            var result = await new OpenAiExtractor(new SecureSettings()).ResearchAsync(CompanyBox.Text, WebsiteBox.Text, _images);
            using var json = System.Text.Json.JsonDocument.Parse(result); var root = json.RootElement;
            ResearchBox.Text = root.GetProperty("summary").GetString() ?? result;
            FillIfBlank(CompanyBox, root, "company"); FillIfBlank(WebsiteBox, root, "website"); FillIfBlank(AddressBox, root, "address"); FillIfBlank(PhoneBox, root, "phone"); FillIfBlank(EmailBox, root, "email");
        }
        catch (Exception error) { System.Windows.MessageBox.Show(error.Message, "Research failed"); } finally { ResearchBusyIndicator.Visibility = System.Windows.Visibility.Collapsed; }
    }

    private static void FillIfBlank(System.Windows.Controls.TextBox box, System.Text.Json.JsonElement root, string property)
    { if (string.IsNullOrWhiteSpace(box.Text) && root.TryGetProperty(property, out var value)) box.Text = value.GetString() ?? ""; }

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
