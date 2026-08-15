using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.PayPal.Contracts;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public static class PayPalHttpClientNames
{
    public const string Api = "PayPal";
}

/// <summary>Optional PayPal request headers (idempotency + representation preference).</summary>
public sealed class PayPalRequestHeaders
{
    public string? RequestId { get; init; }
    public string? Prefer { get; init; }
}

/// <summary>
/// Low-level PayPal HTTP client: attaches the bearer token, serializes/deserializes against the
/// contract, and turns PayPal's error model into <see cref="PaymentGatewayException"/>. It re-auths
/// once on a 401. Request bodies (which may carry card data) are never logged.
/// </summary>
public class PayPalApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PayPalTokenProvider _tokenProvider;
    private readonly ILogger<PayPalApiClient> _logger;

    public PayPalApiClient(
        IHttpClientFactory httpClientFactory,
        PayPalTokenProvider tokenProvider,
        ILogger<PayPalApiClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _tokenProvider = tokenProvider;
        _logger = logger;
    }

    public Task<TResponse> SendAsync<TResponse>(
        HttpMethod method,
        string path,
        object? body = null,
        PayPalRequestHeaders? headers = null,
        CancellationToken cancellationToken = default)
        => SendCoreAsync<TResponse>(method, path, body, headers, expectBody: true, cancellationToken)!;

    /// <summary>Sends a request whose successful response has no body (e.g. void → 204).</summary>
    public async Task SendNoContentAsync(
        HttpMethod method,
        string path,
        object? body = null,
        PayPalRequestHeaders? headers = null,
        CancellationToken cancellationToken = default)
        => await SendCoreAsync<object>(method, path, body, headers, expectBody: false, cancellationToken);

    private async Task<TResponse?> SendCoreAsync<TResponse>(
        HttpMethod method,
        string path,
        object? body,
        PayPalRequestHeaders? headers,
        bool expectBody,
        CancellationToken cancellationToken)
    {
        var response = await SendWithAuthRetryAsync(method, path, body, headers, cancellationToken);
        using (response)
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw BuildException(response.StatusCode, content, method, path);
            }

            if (!expectBody || string.IsNullOrWhiteSpace(content))
            {
                return default;
            }

            try
            {
                return JsonSerializer.Deserialize<TResponse>(content, PayPalJson.Options);
            }
            catch (JsonException ex)
            {
                throw new PaymentGatewayException(
                    $"Could not parse PayPal response for {method} {path}.", (int)response.StatusCode, innerException: ex);
            }
        }
    }

    private async Task<HttpResponseMessage> SendWithAuthRetryAsync(
        HttpMethod method,
        string path,
        object? body,
        PayPalRequestHeaders? headers,
        CancellationToken cancellationToken)
    {
        var response = await SendOnceAsync(method, path, body, headers, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            // Token may have been revoked/expired server-side; re-authenticate once and retry.
            response.Dispose();
            _tokenProvider.Invalidate();
            response = await SendOnceAsync(method, path, body, headers, cancellationToken);
        }
        return response;
    }

    private async Task<HttpResponseMessage> SendOnceAsync(
        HttpMethod method,
        string path,
        object? body,
        PayPalRequestHeaders? headers,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(PayPalHttpClientNames.Api);
        var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);

        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (headers?.RequestId is { Length: > 0 } requestId)
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }
        if (headers?.Prefer is { Length: > 0 } prefer)
        {
            request.Headers.TryAddWithoutValidation("Prefer", prefer);
        }

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, body.GetType(), PayPalJson.Options);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return await client.SendAsync(request, cancellationToken);
    }

    private PaymentGatewayException BuildException(HttpStatusCode statusCode, string content, HttpMethod method, string path)
    {
        PayPalErrorResponse? error = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(content))
            {
                error = JsonSerializer.Deserialize<PayPalErrorResponse>(content, PayPalJson.Options);
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; fall through to a generic message.
        }

        var issues = new List<string>();
        if (error?.Details is not null)
        {
            foreach (var d in error.Details)
            {
                if (!string.IsNullOrEmpty(d.Issue)) issues.Add(d.Issue!);
            }
        }

        var name = error?.Name ?? "PAYPAL_ERROR";
        var firstIssue = issues.Count > 0 ? issues[0] : null;
        var message = BuildMessage(error, firstIssue);

        // Log the failure without the request body (which may contain card data).
        _logger.LogError(
            "PayPal {Method} {Path} failed: {StatusCode} {Name} {Issue} (debug_id={DebugId}).",
            method, path, (int)statusCode, name, firstIssue, error?.DebugId);

        return new PaymentGatewayException(message, (int)statusCode, name, firstIssue, error?.DebugId, issues);
    }

    private static string BuildMessage(PayPalErrorResponse? error, string? firstIssue)
    {
        if (error is null)
        {
            return "PayPal returned an error.";
        }

        var sb = new StringBuilder();
        sb.Append(error.Name ?? "PAYPAL_ERROR");
        if (!string.IsNullOrEmpty(error.Message))
        {
            sb.Append(": ").Append(error.Message);
        }
        if (error.Details is { Count: > 0 })
        {
            var first = error.Details[0];
            if (!string.IsNullOrEmpty(first.Issue))
            {
                sb.Append(" [").Append(first.Issue);
                if (!string.IsNullOrEmpty(first.Description))
                {
                    sb.Append(" - ").Append(first.Description);
                }
                sb.Append(']');
            }
        }
        return sb.ToString();
    }
}
