using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin JSON/HTTP wrapper around the Maxio Advanced Billing API. Knows how to talk HTTP to Maxio;
/// knows nothing about eShopOnWeb's domain (that mapping lives in <see cref="MaxioSubscriptionGateway"/>).
/// </summary>
internal sealed class MaxioApiClient
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private const int MaxAttempts = 3;

    private readonly HttpClient _httpClient;

    public MaxioApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<TResponse?> GetAsync<TResponse>(string relativeUrl, CancellationToken cancellationToken)
        where TResponse : class
    {
        using var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, relativeUrl), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, cancellationToken);
    }

    public async Task<TResponse> PostAsync<TRequest, TResponse>(string relativeUrl, TRequest body, CancellationToken cancellationToken)
        where TResponse : class
    {
        using var response = await SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Post, relativeUrl) { Content = JsonContent.Create(body, options: JsonOptions) },
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, cancellationToken))!;
    }

    /// <summary>
    /// Posts a request carrying a <c>uniqueness_token</c>, treating Maxio's 409 duplicate-submission
    /// response as a non-exceptional "already handled" outcome (returns null) rather than a failure.
    /// See https://.../about-the-api/duplicate-prevention.
    /// </summary>
    public async Task<TResponse?> PostIdempotentAsync<TRequest, TResponse>(string relativeUrl, TRequest body, CancellationToken cancellationToken)
        where TResponse : class
    {
        using var response = await SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Post, relativeUrl) { Content = JsonContent.Create(body, options: JsonOptions) },
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new MaxioApiException(response.StatusCode, $"Maxio API request failed ({(int)response.StatusCode} {response.StatusCode}): {body}");
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage? response = null;
            try
            {
                response = await _httpClient.SendAsync(requestFactory(), cancellationToken);
                var isRetryable = response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
                if (!isRetryable || attempt >= MaxAttempts)
                {
                    return response;
                }

                response.Dispose();
            }
            catch (HttpRequestException) when (attempt < MaxAttempts)
            {
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);
        }
    }
}
