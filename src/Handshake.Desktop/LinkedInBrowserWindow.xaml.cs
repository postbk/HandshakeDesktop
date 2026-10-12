using System.Text.Json;
using System.IO;
using Microsoft.Web.WebView2.Core;

namespace Handshake.Desktop;

public sealed record LinkedInImportResult(string Url, string Title, string VisibleText, string ImageUrl, List<string>? ImageUrls = null);

public partial class LinkedInBrowserWindow : System.Windows.Window
{
    private readonly string _startUrl;
    public LinkedInImportResult? Result { get; private set; }
    public LinkedInBrowserWindow(string person, string company)
    {
        InitializeComponent(); _startUrl = $"https://www.google.com/search?q={Uri.EscapeDataString($"{person} {company} LinkedIn")}"; Loaded += Browser_Loaded;
    }
    private async void Browser_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        try
        {
            var userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Handshake", "WebView2");
            Directory.CreateDirectory(userDataFolder);
            var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await Browser.EnsureCoreWebView2Async(environment);
            Browser.CoreWebView2.Navigate(_startUrl);
        }
        catch (Exception error)
        {
            StatusText.Text = error is UnauthorizedAccessException
                ? "The embedded browser could not access its user data folder. Close Handshake and try again."
                : $"Could not start the embedded browser: {error.Message}";
        }
    }
    private void Back_Click(object sender, System.Windows.RoutedEventArgs e) { if (Browser.CanGoBack) Browser.GoBack(); }
    private void Forward_Click(object sender, System.Windows.RoutedEventArgs e) { if (Browser.CanGoForward) Browser.GoForward(); }
    private async void Import_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        try { var raw = await Browser.CoreWebView2.ExecuteScriptAsync("JSON.stringify({url:location.href||'',title:document.title||'',visibleText:(document.body?document.body.innerText:''),imageUrl:(document.querySelector('meta[property=\\\"og:image\\\"]')||{}).content||'',imageUrls:[...document.images].map(x=>x.currentSrc||x.src).filter(x=>x)})"); var json = JsonSerializer.Deserialize<string>(raw) ?? "{}"; Result = JsonSerializer.Deserialize<LinkedInImportResult>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); if (Result is not null) DialogResult = true; }
        catch (Exception error) { StatusText.Text = error.Message; }
    }
}
