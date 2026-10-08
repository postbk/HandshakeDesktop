using System.Net.Http.Headers;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.IO;
using Handshake.Desktop.Models;

namespace Handshake.Desktop.Services;

public sealed class OpenAiExtractor
{
    private readonly HttpClient _http = new();
    private readonly SecureSettings _settings;

    public OpenAiExtractor(SecureSettings settings) => _settings = settings;

    public async Task<string> ExtractAsync(IEnumerable<CaptureImage> images, CancellationToken cancellationToken = default)
    {
        var key = _settings.GetApiKey();
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Add an OpenAI API key in Settings first.");
        var content = new List<object> { new { type = "input_text", text = "Extract the person's name, company, title, email, phone, website, address, and printed company claims from these business-card images. Return concise JSON." } };
        foreach (var image in images)
        {
            var bytes = await File.ReadAllBytesAsync(image.Path, cancellationToken);
            var mime = Path.GetExtension(image.Path).ToLowerInvariant() == ".png" ? "image/png" : "image/jpeg";
            content.Add(new { type = "input_image", image_url = $"data:{mime};base64,{Convert.ToBase64String(bytes)}", detail = "high" });
        }
        var schema = new { type = "object", additionalProperties = false, properties = new { name = new { type = "string" }, company = new { type = "string" }, title = new { type = "string" }, email = new { type = "string" }, phone = new { type = "string" }, website = new { type = "string" }, address = new { type = "string" }, printed_claims = new { type = "string" } }, required = new[] { "name", "company", "title", "email", "phone", "website", "address", "printed_claims" } };
        var request = new { model = "gpt-4.1-mini", store = false, input = new[] { new { role = "user", content } }, text = new { format = new { type = "json_schema", name = "contact_capture", strict = true, schema } } };
        using var message = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        message.Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(message, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"OpenAI request failed ({(int)response.StatusCode}). Check the API key and account access.");
        using var json = JsonDocument.Parse(body);
        if (!json.RootElement.TryGetProperty("output", out var output)) return body;
        foreach (var item in output.EnumerateArray())
            if (item.TryGetProperty("content", out var parts))
                foreach (var part in parts.EnumerateArray())
                    if (part.TryGetProperty("text", out var text)) return text.GetString() ?? body;
        return body;
    }
}
