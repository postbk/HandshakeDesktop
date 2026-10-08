using Handshake.Desktop.Models;
using Handshake.Desktop.Services;

namespace Handshake.Desktop;

public partial class ReviewWindow : System.Windows.Window
{
    private readonly EventRecord _event;
    private readonly LocalStore _store;
    public ReviewWindow(EventRecord eventRecord, LocalStore store, string raw)
    {
        InitializeComponent(); _event = eventRecord; _store = store; RawBox.Text = raw;
        var text = raw.Replace("\\n", "\n");
        NameBox.Text = Find(text, "name"); CompanyBox.Text = Find(text, "company"); TitleBox.Text = Find(text, "title");
        EmailBox.Text = Find(text, "email"); PhoneBox.Text = Find(text, "phone"); WebsiteBox.Text = Find(text, "website"); AddressBox.Text = Find(text, "address");
    }
    private static string Find(string text, string key)
    {
        var match = System.Text.RegularExpressions.Regex.Match(text, $"[\\\"']{key}[\\\"']\\s*:\\s*[\\\"']([^\\\"']*)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : "";
    }
    private void Save_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text) && string.IsNullOrWhiteSpace(CompanyBox.Text)) { System.Windows.MessageBox.Show("Enter a name or company."); return; }
        _store.SaveContact(new ContactRecord(Guid.NewGuid().ToString(), _event.Id, NameBox.Text.Trim(), CompanyBox.Text.Trim(), TitleBox.Text.Trim(), EmailBox.Text.Trim(), PhoneBox.Text.Trim(), WebsiteBox.Text.Trim(), AddressBox.Text.Trim(), RawBox.Text));
        DialogResult = true;
    }
}
