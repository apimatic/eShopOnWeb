using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Thin HTTP client for the Maxio Advanced Billing (Billing API) REST endpoints.
///
/// API docs (maxio-docs):
/// - Authentication is HTTP Basic over TLS: the API key is the username, the password is the literal "X".
/// - The host is https://{subdomain}.chargify.com; an optional configured BaseUrl overrides it verbatim.
/// - Path parameters may be given as a numeric id or as "handle:{handle}".
/// </summary>
public class MaxioApiClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly string _baseUrl;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioSettings> settings)
    {
        _settings = settings.Value ?? new MaxioSettings();
        _httpClient = httpClient;

        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            throw new InvalidOperationException("Maxio:ApiKey is not configured.");
        }

        _baseUrl = ResolveBaseUrl(_settings);
        _httpClient.BaseAddress = new Uri(_baseUrl);
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_settings.ApiKey}:X")));
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private static string ResolveBaseUrl(MaxioSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            return settings.BaseUrl.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(settings.Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio:Subdomain is not configured and no Maxio:BaseUrl override was provided.");
        }

        return $"https://{settings.Subdomain.TrimEnd('/')}.chargify.com";
    }

    /// <summary>
    /// Sends a request and returns the parsed JSON body regardless of the status code,
    /// so callers can decide how to handle non-success statuses.
    /// </summary>
    public async Task<(int StatusCode, JsonElement Body)> SendAsync(
        HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);

        if (body is not null)
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();

        JsonElement parsed;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
            parsed = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            parsed = JsonDocument.Parse("{}").RootElement.Clone();
        }

        return ((int)response.StatusCode, parsed);
    }

    /// <summary>
    /// Sends a request, throwing a <see cref="MaxioApiException"/> on any non-success status.
    /// </summary>
    public async Task<JsonElement> SendExpectedAsync(
        HttpMethod method, string path, object? body = null)
    {
        var (statusCode, parsed) = await SendAsync(method, path, body);
        if (statusCode < 200 || statusCode >= 300)
        {
            throw new MaxioApiException(statusCode, parsed.GetRawText());
        }

        return parsed;
    }

    public async Task<JsonElement> GetAsync(string path) =>
        await SendExpectedAsync(HttpMethod.Get, path);

    public async Task<JsonElement> PostAsync(string path, object body) =>
        await SendExpectedAsync(HttpMethod.Post, path, body);
}
