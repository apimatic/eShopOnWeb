using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Low-level HTTP client for the Maxio Advanced Billing API. The API contract comes
/// from maxio-spec/openapi.yaml: basic auth with the API key as username and the
/// literal password "x", JSON bodies, and a site-templated base URL
/// (https://{site}.chargify.com for US, https://{site}.ebilling.maxio.com for EU).
/// </summary>
public class MaxioApiClient
{
    private const string BasicAuthPassword = "x";
    private const int DefaultPerPage = 100;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly Microsoft.eShopWeb.ApplicationCore.Interfaces.IAppLogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioOptions> options,
        Microsoft.eShopWeb.ApplicationCore.Interfaces.IAppLogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        var baseUrl = ResolveBaseUrl(_options);
        _httpClient.BaseAddress = new Uri(baseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(60);
        var headerValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:{BasicAuthPassword}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", headerValue);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>
    /// Sends a GET and deserializes a single-object envelope. Returns null on 404.
    /// </summary>
    public async Task<TResponse?> GetAsync<TResponse>(string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return default;
        }
        await EnsureSuccessAsync(response, path);
        return await DeserializeAsync<TResponse>(response, cancellationToken);
    }

    /// <summary>
    /// Sends a GET and deserializes a JSON array envelope.
    /// </summary>
    public async Task<IReadOnlyList<TResponse>> GetListAsync<TResponse>(string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken);
        await EnsureSuccessAsync(response, path);
        var items = await DeserializeAsync<List<TResponse>>(response, cancellationToken);
        return items ?? new List<TResponse>();
    }

    public async Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest request, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, path, request, cancellationToken);
        await EnsureSuccessAsync(response, path);
        var result = await DeserializeAsync<TResponse>(response, cancellationToken);
        return result!;
    }

    /// <summary>
    /// Sends GETs that page through an array endpoint, invoking the predicate for each
    /// page. Stops when the predicate returns true, or a page comes back empty or short.
    /// </summary>
    public async Task ScanPagedAsync<TItem>(string basePath, int perPage, Func<IReadOnlyList<TItem>, bool> onPage, CancellationToken cancellationToken)
    {
        var page = 1;
        while (true)
        {
            var separator = basePath.Contains('?') ? '&' : '?';
            var path = $"{basePath}{separator}page={page}&per_page={perPage}";
            var items = await GetListAsync<TItem>(path, cancellationToken);
            if (items.Count == 0)
            {
                break;
            }
            if (onPage(items) || items.Count < perPage)
            {
                break;
            }
            page++;
        }
    }

    private static string ResolveBaseUrl(MaxioOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException(
                "Maxio integration is not configured: missing 'Maxio:ApiKey'. Set it from the MAXIO_API_KEY environment variable via user-secrets.");
        }
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            return options.BaseUrl.TrimEnd('/') + "/";
        }
        if (string.IsNullOrWhiteSpace(options.Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio integration is not configured: missing 'Maxio:Subdomain'. Set it from the MAXIO_SITE_SUBDOMAIN environment variable via user-secrets (or set 'Maxio:BaseUrl' directly).");
        }
        return string.Equals(options.Environment, "EU", StringComparison.OrdinalIgnoreCase)
            ? $"https://{options.Subdomain}.ebilling.maxio.com/"
            : $"https://{options.Subdomain}.chargify.com/";
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? requestBody, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (requestBody != null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(requestBody, SerializerOptions), Encoding.UTF8, "application/json");
        }
        _logger.LogInformation($"Maxio API {method} {path}");
        return await _httpClient.SendAsync(request, cancellationToken);
    }

    private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, SerializerOptions, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string path)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }
        var body = await response.Content.ReadAsStringAsync();
        throw new MaxioApiException((int)response.StatusCode, body,
            $"Maxio API call to '{path}' failed with status {(int)response.StatusCode} {response.StatusCode}: {body}");
    }
}
