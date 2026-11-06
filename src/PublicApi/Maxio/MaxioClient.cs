using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Thin HTTP client for the Maxio (Advanced Billing) REST API. Responses are returned as JSON nodes.
/// </summary>
public class MaxioClient
{
    private readonly HttpClient _http;
    private readonly MaxioOptions _options;

    public MaxioClient(HttpClient http, IOptions<MaxioOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public Task<JsonNode?> GetAsync(string path) => SendAsync(HttpMethod.Get, path, null);
    public Task<JsonNode?> PostAsync(string path, JsonNode? body = null) => SendAsync(HttpMethod.Post, path, body);
    public Task<JsonNode?> PutAsync(string path, JsonNode? body = null) => SendAsync(HttpMethod.Put, path, body);
    public Task<JsonNode?> DeleteAsync(string path, JsonNode? body = null) => SendAsync(HttpMethod.Delete, path, body);

    /// <summary>GET that returns null instead of throwing when Maxio answers 404.</summary>
    public async Task<JsonNode?> GetOrNullAsync(string path)
    {
        try
        {
            return await GetAsync(path);
        }
        catch (MaxioException ex) when (ex.MaxioStatusCode == 404)
        {
            return null;
        }
    }

    private string BaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            return _options.BaseUrl.TrimEnd('/');
        }
        if (string.IsNullOrWhiteSpace(_options.Subdomain))
        {
            throw new ApiException(500, "Billing is not configured: Maxio:Subdomain (or Maxio:BaseUrl) is missing.");
        }
        var host = (_options.Environment ?? "US").Trim().ToUpperInvariant() switch
        {
            "US" => "chargify.com",
            "EU" => "chargify.eu",
            _ => throw new ApiException(500, "Billing is not configured: Maxio:Environment must be US or EU.")
        };
        return $"https://{_options.Subdomain}.{host}";
    }

    private async Task<JsonNode?> SendAsync(HttpMethod method, string path, JsonNode? body)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new ApiException(500, "Billing is not configured: Maxio:ApiKey is missing.");
        }

        using var request = new HttpRequestMessage(method, BaseAddress() + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(_options.ApiKey + ":x")));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request);
        }
        catch (HttpRequestException)
        {
            throw new ApiException(502, "The billing system could not be reached.");
        }
        catch (TaskCanceledException)
        {
            throw new ApiException(504, "The billing system did not respond in time.");
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
            {
                if (string.IsNullOrWhiteSpace(text)) return null;
                try { return JsonNode.Parse(text); }
                catch (System.Text.Json.JsonException)
                {
                    throw new ApiException(502, "The billing system returned an unreadable response.");
                }
            }
            throw new MaxioException((int)response.StatusCode, ExtractErrors(text));
        }
    }

    private static string ExtractErrors(string text)
    {
        var messages = new List<string>();
        try
        {
            var node = JsonNode.Parse(text);
            Collect(node?["errors"] ?? node?["error"], messages);
        }
        catch (System.Text.Json.JsonException)
        {
        }
        return messages.Count > 0 ? string.Join("; ", messages) : "The billing system rejected the request.";
    }

    private static void Collect(JsonNode? node, List<string> messages)
    {
        switch (node)
        {
            case JsonArray array:
                foreach (var item in array) Collect(item, messages);
                break;
            case JsonObject obj:
                foreach (var kv in obj) Collect(kv.Value, messages);
                break;
            case JsonValue value:
                var s = value.ToString();
                if (!string.IsNullOrWhiteSpace(s)) messages.Add(s);
                break;
        }
    }
}

/// <summary>An error response from Maxio, translated to an HTTP status for our caller.</summary>
public class MaxioException : ApiException
{
    public int MaxioStatusCode { get; }

    public MaxioException(int maxioStatus, string message) : base(Translate(maxioStatus), Describe(maxioStatus, message))
    {
        MaxioStatusCode = maxioStatus;
    }

    private static int Translate(int status) => status switch
    {
        404 => 404,
        400 or 422 => 422,
        409 => 409,
        401 or 403 => 502,
        429 => 503,
        _ => 502
    };

    private static string Describe(int status, string message) => status switch
    {
        401 or 403 => "The billing system refused our credentials.",
        429 => "The billing system is rate limiting requests; try again shortly.",
        >= 500 => "The billing system failed to process the request.",
        _ => message
    };
}
