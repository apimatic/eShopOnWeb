using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// HTTP client wrapper for the Maxio Billing API.
/// - HTTP Basic authentication with the API key as the username and "X" as the
///   password, per the Billing API authentication documentation.
/// - JSON payloads with the .json endpoint suffix.
/// - Bounded retries on 429 (rate limit) and transient 5xx responses, with
///   exponential backoff and no parallelism, per Billing API rate-limit guidance.
/// </summary>
public class MaxioApiClient
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(1);

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioApiClient> _logger;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioOptions> options,
        ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        _httpClient.BaseAddress = new Uri(options.Value.ResolveBaseUrl() + "/");
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.Value.ApiKey}:X")));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.Timeout = TimeSpan.FromSeconds(60);
    }

    /// <summary>
    /// Performs a GET against the Billing API and deserializes the response.
    /// Returns null when the API responds with 404 Not Found.
    /// </summary>
    public async Task<T?> GetAsync<T>(string resourcePath, CancellationToken cancellationToken = default)
        where T : class
    {
        var response = await SendAsync(HttpMethod.Get, resourcePath, requestBody: null, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response);
        return await DeserializeAsync<T>(response, cancellationToken);
    }

    /// <summary>
    /// Performs a POST against the Billing API and deserializes the response.
    /// Returns null when the API responds with 404 Not Found.
    /// </summary>
    public async Task<T?> PostAsync<T>(string resourcePath, object requestBody, CancellationToken cancellationToken = default)
        where T : class
    {
        var response = await SendAsync(HttpMethod.Post, resourcePath, requestBody, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response);
        return await DeserializeAsync<T>(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string resourcePath,
        object? requestBody, CancellationToken cancellationToken)
    {
        Exception? lastError = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            using var request = new HttpRequestMessage(method, resourcePath);
            if (requestBody is not null)
            {
                request.Content = new StringContent(
                    JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            }

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException ex) when (attempt < MaxAttempts)
            {
                lastError = ex;
                _logger.LogWarning(ex, "Maxio request attempt {Attempt}/{Max} to {Path} failed; retrying.",
                    attempt, MaxAttempts, resourcePath);
                await Task.Delay(FirstRetryDelay * attempt, cancellationToken);
                continue;
            }

            if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
            {
                if (attempt < MaxAttempts)
                {
                    _logger.LogWarning(
                        "Maxio request to {Path} returned {StatusCode}; backing off before retry {Attempt}.",
                        resourcePath, (int)response.StatusCode, attempt + 1);
                    response.Dispose();
                    await Task.Delay(FirstRetryDelay * attempt, cancellationToken);
                    continue;
                }
            }

            return response;
        }

        throw new MaxioApiException(
            $"Maxio Billing API call to {resourcePath} failed after {MaxAttempts} attempts.",
            statusCode: 0, responseBody: null, inner: lastError);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = response.Content is null ? null : await response.Content.ReadAsStringAsync();
        throw MaxioApiException.FromResponse(response, body ?? string.Empty);
    }

    private static async Task<T> DeserializeAsync<T>(HttpResponseMessage response,
        CancellationToken cancellationToken) where T : class
    {
        var body = response.Content is null ? null : await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new MaxioApiException("Maxio Billing API returned an empty response body.",
                statusCode: (int)response.StatusCode, responseBody: body);
        }

        return JsonSerializer.Deserialize<T>(body, SerializerOptions)
            ?? throw new MaxioApiException(
                $"Maxio Billing API response could not be mapped to {typeof(T).Name}.",
                statusCode: (int)response.StatusCode, responseBody: body);
    }
}