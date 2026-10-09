using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace Handshake.Desktop;

public sealed record LinkedInImportResult(string Url, string Title, string VisibleText, string ImageUrl);

public partial class LinkedInBrowserWindow : System.Windows.Window
{
    private readonly string _startUrl;
    public LinkedInImportResult? Result { get; private set; }
    public LinkedInBrowserWindow(string person, string company)
    {
        InitializeComponent(); _startUrl = $"https://www.google.com/search?q={Uri.EscapeDataString($"{person} {company} LinkedIn")}"; Loaded += Browser_Loaded;
    }
    private async void Browser_Loaded(object sender, System.Windows.RoutedEventArgs e) { try { await Browser.EnsureCoreWebView2Async(); Browser.CoreWebView2.Navigate(_startUrl); } catch (Exception error) { StatusText.Text = error.Message; } }
    private void Back_Click(object sender, System.Windows.RoutedEventArgs e) { if (Browser.CanGoBack) Browser.GoBack(); }
    private void Forward_Click(object sender, System.Windows.RoutedEventArgs e) { if (Browser.CanGoForward) Browser.GoForward(); }
    private async void Import_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        try { var raw = await Browser.CoreWebView2.ExecuteScriptAsync("JSON.stringify({url:location.href,title:document.title,text:(document.body?document.body.innerText:''),image:(document.querySelector('meta[property=\\\"og:image\\\"]')||{}).content||''})"); var json = JsonSerializer.Deserialize<string>(raw) ?? "{}"; Result = JsonSerializer.Deserialize<LinkedInImportResult>(json); if (Result is not null) DialogResult = true; }
        catch (Exception error) { StatusText.Text = error.Message; }
    }
}
