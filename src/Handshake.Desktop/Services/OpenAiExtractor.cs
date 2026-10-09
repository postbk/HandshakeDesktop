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
        var content = new List<object> { new { type = "input_text", text = "Extract every distinct person/business card visible in these images. Return one contact object per card, even when multiple cards appear in one image. Include name, company, title, email, phone, website, address, and printed company claims. Return concise JSON." } };
        foreach (var image in images)
        {
            var bytes = await File.ReadAllBytesAsync(image.Path, cancellationToken);
            var mime = Path.GetExtension(image.Path).ToLowerInvariant() == ".png" ? "image/png" : "image/jpeg";
            content.Add(new { type = "input_image", image_url = $"data:{mime};base64,{Convert.ToBase64String(bytes)}", detail = "high" });
        }
        var contactSchema = new { type = "object", additionalProperties = false, properties = new { name = new { type = "string" }, company = new { type = "string" }, title = new { type = "string" }, email = new { type = "string" }, phone = new { type = "string" }, website = new { type = "string" }, address = new { type = "string" }, printed_claims = new { type = "string" } }, required = new[] { "name", "company", "title", "email", "phone", "website", "address", "printed_claims" } };
        var schema = new { type = "object", additionalProperties = false, properties = new { contacts = new { type = "array", items = contactSchema } }, required = new[] { "contacts" } };
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

    public async Task<string> ResearchAsync(string company, string website, IEnumerable<CaptureImage>? images = null, CancellationToken cancellationToken = default)
    {
        var key = _settings.GetApiKey();
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Add an OpenAI API key in Settings first.");
        var content = new List<object> { new { type = "input_text", text = $"Research this exact company and person for a trade-show follow-up. Company: {company}. Website hint: {website}. Use the attached business-card image to read logos, printed domains, email domains, capability claims, and the person's name. Use those clues to disambiguate generic company names. Summarize what the company does, products/services, and markets. Also look for a professional headshot of the named person. Return a photo_url only when it is a reasonably confident match for this person; otherwise leave photo_url empty. Cite sources inline and distinguish company claims from independently verified information. Do not include a list of follow-up questions." } };
        foreach (var image in images ?? []) { var bytes = await File.ReadAllBytesAsync(image.Path, cancellationToken); content.Add(new { type = "input_image", image_url = $"data:image/jpeg;base64,{Convert.ToBase64String(bytes)}", detail = "high" }); }
        var schema = new { type = "object", additionalProperties = false, properties = new { company = new { type = "string" }, website = new { type = "string" }, address = new { type = "string" }, phone = new { type = "string" }, email = new { type = "string" }, summary = new { type = "string" }, photo_url = new { type = "string" }, photo_confidence = new { type = "string", @enum = new[] { "high", "medium", "low", "none" } } }, required = new[] { "company", "website", "address", "phone", "email", "summary", "photo_url", "photo_confidence" } };
        var request = new { model = "gpt-4.1", store = false, tools = new[] { new { type = "web_search_preview" } }, input = new[] { new { role = "user", content } }, text = new { format = new { type = "json_schema", name = "company_research", strict = true, schema } } };
        using var message = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        message.Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(message, cancellationToken); var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Research request failed ({(int)response.StatusCode}).");
        using var json = JsonDocument.Parse(body); if (!json.RootElement.TryGetProperty("output", out var output)) return body;
        foreach (var item in output.EnumerateArray()) if (item.TryGetProperty("content", out var parts)) foreach (var part in parts.EnumerateArray()) if (part.TryGetProperty("text", out var text)) return text.GetString() ?? body;
        return body;
    }

    public async Task<string> AnalyzeMemoAsync(string transcript, string contact, CancellationToken cancellationToken = default)
    {
        var key = _settings.GetApiKey(); if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Add an OpenAI API key in Settings first.");
        var schema = new { type = "object", additionalProperties = false, properties = new { bullets = new { type = "array", items = new { type = "string" } }, follow_ups = new { type = "array", items = new { type = "string" } } }, required = new[] { "bullets", "follow_ups" } };
        var request = new { model = "gpt-4.1-mini", store = false, input = $"Clean up this personal meeting transcript for contact {contact}. Extract concise factual bullets about the person/company/meeting. Extract only explicit promised or requested actions as follow_ups. Do not invent tasks. Transcript:\n{transcript}", text = new { format = new { type = "json_schema", name = "contact_memo", strict = true, schema } } };
        using var message = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses") { Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json") }; message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        using var response = await _http.SendAsync(message, cancellationToken); var body = await response.Content.ReadAsStringAsync(cancellationToken); if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Memo analysis failed ({(int)response.StatusCode}).");
        using var json = JsonDocument.Parse(body); if (!json.RootElement.TryGetProperty("output", out var output)) return body; foreach (var item in output.EnumerateArray()) if (item.TryGetProperty("content", out var parts)) foreach (var part in parts.EnumerateArray()) if (part.TryGetProperty("text", out var text)) return text.GetString() ?? body; return body;
    }

    public async Task<string> TranscribeAsync(string audioPath, CancellationToken cancellationToken = default)
    {
        var key = _settings.GetApiKey(); if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Add an OpenAI API key in Settings first.");
        using var form = new MultipartFormDataContent(); form.Add(new StringContent("gpt-4o-mini-transcribe"), "model");
        await using var stream = File.OpenRead(audioPath); var audio = new StreamContent(stream); audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav"); form.Add(audio, "file", Path.GetFileName(audioPath));
        using var message = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/audio/transcriptions") { Content = form }; message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        using var response = await _http.SendAsync(message, cancellationToken); var body = await response.Content.ReadAsStringAsync(cancellationToken); if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Transcription failed ({(int)response.StatusCode}).");
        using var json = JsonDocument.Parse(body); return json.RootElement.TryGetProperty("text", out var text) ? text.GetString() ?? "" : body;
    }
}
