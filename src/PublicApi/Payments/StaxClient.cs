using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>An error the API reports to the caller with an HTTP status and a reason.</summary>
public class ApiException : Exception
{
    public int StatusCode { get; }
    public ApiException(int statusCode, string message) : base(message) { StatusCode = statusCode; }
}

public class StaxClient
{
    public const string DefaultBaseUrl = "https://apiprod.fattlabs.com";

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly IConfiguration _config;

    public StaxClient(HttpClient http, IConfiguration config)
    {
        _http = http;
        _config = config;
    }

    private string BaseUrl
    {
        get
        {
            var url = _config["Stax:BaseUrl"];
            return string.IsNullOrWhiteSpace(url) ? DefaultBaseUrl : url;
        }
    }

    public bool HasPlatformKey => !string.IsNullOrWhiteSpace(_config["Stax:PlatformApiKey"]);

    public Task<JsonNode> GetAsync(string path, CancellationToken ct = default) => SendAsync(HttpMethod.Get, path, null, false, ct);
    public Task<JsonNode> PostAsync(string path, object? body, CancellationToken ct = default) => SendAsync(HttpMethod.Post, path, body, false, ct);
    public Task<JsonNode> PutAsync(string path, object? body, CancellationToken ct = default) => SendAsync(HttpMethod.Put, path, body, false, ct);
    public Task<JsonNode> DeleteAsync(string path, CancellationToken ct = default) => SendAsync(HttpMethod.Delete, path, null, false, ct);
    public Task<JsonNode> PlatformPostAsync(string path, object? body, CancellationToken ct = default) => SendAsync(HttpMethod.Post, path, body, true, ct);

    private async Task<JsonNode> SendAsync(HttpMethod method, string path, object? body, bool platform, CancellationToken ct)
    {
        var key = platform ? _config["Stax:PlatformApiKey"] : _config["Stax:ApiKey"];
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ApiException(platform ? 422 : 503,
                platform ? "Stax platform key is not configured (Stax:PlatformApiKey)." : "Stax API key is not configured (Stax:ApiKey).");
        }

        var url = BaseUrl.TrimEnd('/') + "/" + path.TrimStart('/');
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body != null)
        {
            request.Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        string text;
        try
        {
            response = await _http.SendAsync(request, ct);
            text = await response.Content.ReadAsStringAsync(ct);
        }
        catch (HttpRequestException ex)
        {
            throw new ApiException(502, "Stax could not be reached: " + ex.Message);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ApiException(504, "Stax did not answer in time.");
        }

        JsonNode? json = null;
        if (!string.IsNullOrWhiteSpace(text))
        {
            try { json = JsonNode.Parse(text); } catch (System.Text.Json.JsonException) { }
        }

        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            var reason = ExtractReason(json) ?? $"Stax answered {status}.";
            if (status == 404 || reason.Contains("could not be found", StringComparison.OrdinalIgnoreCase)) throw new ApiException(404, reason);
            // Stax rejections of the request itself stay 4xx; anything else is a gateway problem.
            if (status >= 400 && status < 500 && status != 401 && status != 403) throw new ApiException(422, reason);
            throw new ApiException(502, "Stax error: " + reason);
        }

        return json ?? new JsonObject();
    }

    private static string? ExtractReason(JsonNode? json)
    {
        if (json is not JsonObject obj) return null;
        var parts = new List<string>();
        void Add(JsonNode? n)
        {
            switch (n)
            {
                case JsonValue v when v.TryGetValue<string>(out var str): parts.Add(str); break;
                case JsonArray a: foreach (var i in a) Add(i); break;
                case JsonObject o: foreach (var kv in o) Add(kv.Value); break;
            }
        }
        if (obj["error"] != null) Add(obj["error"]);
        if (parts.Count == 0 && obj["message"] != null) Add(obj["message"]);
        if (parts.Count == 0) Add(obj);
        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    public static string Q(string s) => Uri.EscapeDataString(s);
}
