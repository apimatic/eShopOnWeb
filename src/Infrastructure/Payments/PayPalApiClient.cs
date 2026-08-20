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
using Microsoft.eShopWeb.Infrastructure.Payments.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalApiClient
{
    private readonly HttpClient _httpClient;
    private readonly PayPalAccessTokenProvider _tokenProvider;
    private readonly ILogger<PayPalApiClient> _logger;

    public PayPalApiClient(
        HttpClient httpClient,
        IOptions<PayPalOptions> options,
        PayPalAccessTokenProvider tokenProvider,
        ILogger<PayPalApiClient> logger)
    {
        _httpClient = httpClient;
        _tokenProvider = tokenProvider;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(EnsureTrailingSlash(options.Value.ResolveBaseUrl()));
        _httpClient.Timeout = TimeSpan.FromSeconds(45);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<TResponse> SendAsync<TResponse>(
        HttpMethod method,
        string relativeUrl,
        object? body,
        string? paypalRequestId,
        CancellationToken cancellationToken)
    {
        var response = await SendRawAsync(method, relativeUrl, body, paypalRequestId, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return default!;
        }

        return JsonSerializer.Deserialize<TResponse>(payload, PayPalJson.DeserializerOptions)
               ?? throw new PaymentException("PayPal returned an empty response body.", 502);
    }

    public async Task SendAsync(
        HttpMethod method,
        string relativeUrl,
        object? body,
        string? paypalRequestId,
        CancellationToken cancellationToken)
    {
        await SendRawAsync(method, relativeUrl, body, paypalRequestId, cancellationToken);
    }

    public async Task<HttpResponseMessage> SendRawAsync(
        HttpMethod method,
        string relativeUrl,
        object? body,
        string? paypalRequestId,
        CancellationToken cancellationToken)
    {
        var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(method, relativeUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (!string.IsNullOrWhiteSpace(paypalRequestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", paypalRequestId);
        }

        if (body != null)
        {
            var json = JsonSerializer.Serialize(body, PayPalJson.SerializerOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        _logger.LogInformation("PayPal {Method} {Url}", method.Method, relativeUrl);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            throw new PaymentException("Unable to reach PayPal.", ex, 502);
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
        throw MapPayPalError(response.StatusCode, errorBody);
    }

    internal static PaymentException MapPayPalError(HttpStatusCode statusCode, string body)
    {
        PayPalErrorDto? error = null;
        try
        {
            error = JsonSerializer.Deserialize<PayPalErrorDto>(body, PayPalJson.DeserializerOptions);
        }
        catch (JsonException)
        {
            // Body is not the documented error model; fall through with a generic message.
        }

        var issue = error?.Details is { Count: > 0 } ? error.Details[0].Issue : null;
        var description = error?.Details is { Count: > 0 } ? error.Details[0].Description : null;
        var name = error?.Name;
        var message = error?.Message;

        var mappedStatus = (int)statusCode switch
        {
            400 or 404 or 409 or 422 => (int)statusCode == 422 ? 400 : (int)statusCode,
            401 or 403 => 502,
            _ => 502
        };

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(name)) parts.Add(name!);
        if (!string.IsNullOrWhiteSpace(issue)) parts.Add(issue!);
        if (!string.IsNullOrWhiteSpace(description)) parts.Add(description!);
        else if (!string.IsNullOrWhiteSpace(message)) parts.Add(message!);
        if (parts.Count == 0)
        {
            parts.Add($"PayPal request failed with HTTP {(int)statusCode}.");
        }

        return new PaymentException(string.Join(": ", parts), mappedStatus, issue ?? name);
    }

    private static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : url + "/";
}
