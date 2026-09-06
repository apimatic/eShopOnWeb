using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin transport over the Maxio Billing API: JSON in, JSON out, documented failures translated into
/// <see cref="MaxioApiException"/>. It holds no knowledge of subscriptions — that lives in
/// <see cref="MaxioSubscriptionService"/>.
/// </summary>
public class MaxioApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// GETs a resource, returning default(T) when the API reports it does not exist. "Not found" is a
    /// normal answer for the lookup endpoints this integration relies on, not a failure.
    /// </summary>
    internal async Task<T?> GetOrDefaultAsync<T>(string relativeUrl, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
        using var response = await SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        await EnsureSuccessAsync(response, HttpMethod.Get, relativeUrl, cancellationToken);
        return await ReadAsync<T>(response, HttpMethod.Get, relativeUrl, cancellationToken);
    }

    /// <summary>POSTs a JSON body and deserializes the response.</summary>
    internal async Task<T> PostAsync<T>(string relativeUrl, object body, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(body, body.GetType(), MaxioJson.Options);

        using var request = new HttpRequestMessage(HttpMethod.Post, relativeUrl)
        {
            // A byte-backed content can be replayed, which is what lets the resilience handler retry.
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        using var response = await SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, HttpMethod.Post, relativeUrl, cancellationToken);

        var result = await ReadAsync<T>(response, HttpMethod.Post, relativeUrl, cancellationToken);
        if (result is null)
        {
            throw new MaxioApiException(
                $"Maxio returned an empty body for POST {PathOf(relativeUrl)}.",
                HttpMethod.Post, PathOf(relativeUrl), response.StatusCode);
        }

        return result;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            // HttpClient surfaces its own timeout as a cancellation that the caller did not ask for.
            throw new MaxioApiException(
                $"The request to Maxio ({request.Method} {PathOf(request.RequestUri)}) timed out.",
                request.Method, PathOf(request.RequestUri), innerException: ex);
        }
        catch (HttpRequestException ex)
        {
            throw new MaxioApiException(
                $"Could not reach Maxio ({request.Method} {PathOf(request.RequestUri)}): {ex.Message}",
                request.Method, PathOf(request.RequestUri), innerException: ex);
        }
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, HttpMethod method, string relativeUrl,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var path = PathOf(relativeUrl);
        var body = await ReadBodyAsync(response, cancellationToken);
        var errors = MaxioErrorReader.Read(body);

        _logger.LogWarning("Maxio {Method} {Path} failed with {StatusCode}: {Errors}",
            method, path, (int)response.StatusCode,
            errors.Count > 0 ? string.Join("; ", errors) : "(no error details)");

        throw new MaxioApiException(BuildMessage(method, path, response.StatusCode, errors),
            method, path, response.StatusCode, errors);
    }

    private static string BuildMessage(HttpMethod method, string path, HttpStatusCode statusCode,
        IReadOnlyList<string> errors)
    {
        var detail = errors.Count > 0
            ? string.Join("; ", errors)
            : statusCode switch
            {
                HttpStatusCode.Unauthorized => "the configured Maxio API key was rejected",
                HttpStatusCode.Forbidden => "the configured Maxio API key is not allowed to do this",
                HttpStatusCode.TooManyRequests => "the request was throttled by Maxio",
                _ => "no error details were returned"
            };

        return $"Maxio rejected {method} {path} with {(int)statusCode} {statusCode}: {detail}.";
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, HttpMethod method, string relativeUrl,
        CancellationToken cancellationToken)
    {
        var body = await ReadBodyAsync(response, cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(body, MaxioJson.Options);
        }
        catch (JsonException ex)
        {
            throw new MaxioApiException(
                $"Could not read the Maxio response for {method} {PathOf(relativeUrl)}: {ex.Message}",
                method, PathOf(relativeUrl), response.StatusCode, innerException: ex);
        }
    }

    private static Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        response.Content.ReadAsStringAsync(cancellationToken);

    /// <summary>
    /// Strips the query string so paths are safe to log and to embed in error messages: lookups carry
    /// customer references there.
    /// </summary>
    private static string PathOf(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return "(unknown)";
        }

        var queryStart = url.IndexOf('?');
        return queryStart < 0 ? url : url[..queryStart];
    }

    private static string PathOf(Uri? uri) => uri is null ? "(unknown)" : PathOf(uri.AbsolutePath);
}
