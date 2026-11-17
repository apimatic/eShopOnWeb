using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class FinixClient : IFinixClient
{
    private const string FinixApiVersion = "2022-02-01";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<FinixClient> _logger;

    public FinixClient(HttpClient httpClient, ILogger<FinixClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public Task<FinixIdentity> CreateIdentityAsync(
        FinixCreateIdentityRequest request,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<FinixIdentity>(HttpMethod.Post, "identities", request, cancellationToken);
    }

    public Task<FinixPaymentInstrument> CreatePaymentInstrumentAsync(
        FinixCreatePaymentInstrumentRequest request,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<FinixPaymentInstrument>(
            HttpMethod.Post, "payment_instruments", request, cancellationToken);
    }

    public Task<FinixPaymentInstrument> DisablePaymentInstrumentAsync(
        string paymentInstrumentId,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<FinixPaymentInstrument>(
            HttpMethod.Put,
            $"payment_instruments/{Uri.EscapeDataString(paymentInstrumentId)}",
            new FinixUpdatePaymentInstrumentRequest { Enabled = false },
            cancellationToken);
    }

    public async Task<FinixAuthorization> CreateAuthorizationAsync(
        FinixCreateAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await SendAsync<FinixAuthorization>(
                HttpMethod.Post, "authorizations", request, cancellationToken);
        }
        catch (FinixException ex) when (
            !string.IsNullOrWhiteSpace(ex.AuthorizationId)
            && ex.Message.Contains("Duplicate", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Finix reported a duplicate authorization for idempotency key {IdempotencyId}; returning the existing authorization {AuthorizationId}.",
                request.IdempotencyId,
                ex.AuthorizationId);
            return await GetAuthorizationAsync(ex.AuthorizationId!, cancellationToken);
        }
    }

    public Task<FinixAuthorization> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<FinixAuthorization>(
            HttpMethod.Get,
            $"authorizations/{Uri.EscapeDataString(authorizationId)}",
            null,
            cancellationToken);
    }

    public Task<FinixAuthorization> CaptureAuthorizationAsync(
        string authorizationId,
        long captureAmountCents,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<FinixAuthorization>(
            HttpMethod.Put,
            $"authorizations/{Uri.EscapeDataString(authorizationId)}",
            new FinixCaptureAuthorizationRequest { CaptureAmount = captureAmountCents },
            cancellationToken);
    }

    public Task<FinixAuthorization> VoidAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<FinixAuthorization>(
            HttpMethod.Put,
            $"authorizations/{Uri.EscapeDataString(authorizationId)}",
            new FinixVoidAuthorizationRequest { VoidMe = true },
            cancellationToken);
    }

    public Task<FinixTransfer> CreateRefundAsync(
        string transferId,
        long refundAmountCents,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<FinixTransfer>(
            HttpMethod.Post,
            $"transfers/{Uri.EscapeDataString(transferId)}/reversals",
            new FinixCreateRefundRequest { RefundAmount = refundAmountCents },
            cancellationToken);
    }

    public Task<FinixTransfer> GetTransferAsync(
        string transferId,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<FinixTransfer>(
            HttpMethod.Get,
            $"transfers/{Uri.EscapeDataString(transferId)}",
            null,
            cancellationToken);
    }

    public async Task<IReadOnlyList<FinixAuthorization>> ListAuthorizationsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<FinixAuthorization>();
        string? cursor = null;
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        var pageNumber = 0;

        while (pageNumber++ < 1000)
        {
            var url = BuildListUrl("authorizations", from, to, cursor);
            var page = await SendAsync<FinixAuthorizationListResponse>(
                HttpMethod.Get, url, null, cancellationToken);

            var items = page.Embedded?.Authorizations;
            if (items is null || items.Count == 0)
            {
                break;
            }

            results.AddRange(items);

            cursor = page.Page?.NextCursor;
            if (string.IsNullOrWhiteSpace(cursor) || !seenCursors.Add(cursor))
            {
                break;
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<FinixTransfer>> ListTransfersAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<FinixTransfer>();
        string? cursor = null;
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        var pageNumber = 0;

        while (pageNumber++ < 1000)
        {
            var url = BuildListUrl("transfers", from, to, cursor);
            var page = await SendAsync<FinixTransferListResponse>(
                HttpMethod.Get, url, null, cancellationToken);

            var items = page.Embedded?.Transfers;
            if (items is null || items.Count == 0)
            {
                break;
            }

            results.AddRange(items);

            cursor = page.Page?.NextCursor;
            if (string.IsNullOrWhiteSpace(cursor) || !seenCursors.Add(cursor))
            {
                break;
            }
        }

        return results;
    }

    private static string BuildListUrl(
        string resource,
        DateTimeOffset from,
        DateTimeOffset to,
        string? cursor)
    {
        var query = new StringBuilder();
        query.Append("limit=100");
        query.Append("&created_at.gte=");
        query.Append(Uri.EscapeDataString(from.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss")));
        query.Append("&created_at.lte=");
        query.Append(Uri.EscapeDataString(to.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss")));

        if (!string.IsNullOrWhiteSpace(cursor))
        {
            query.Append("&after_cursor=");
            query.Append(Uri.EscapeDataString(cursor));
        }

        return $"{resource}?{query}";
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string relativeUrl,
        object? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, relativeUrl);
        request.Headers.Add("Finix-Version", FinixApiVersion);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var exception = BuildException(response.StatusCode, responseBody);
            _logger.LogWarning(
                "Finix {Method} {Path} failed with {StatusCode}: {Code} {Message}",
                method.Method,
                relativeUrl,
                (int)response.StatusCode,
                exception.ErrorCode,
                exception.Message);
            throw exception;
        }

        if (string.IsNullOrWhiteSpace(responseBody))
        {
            throw new FinixException(
                (int)response.StatusCode,
                "EMPTY_RESPONSE",
                "Finix returned an empty response body.");
        }

        var result = JsonSerializer.Deserialize<T>(responseBody, JsonOptions);
        if (result is null)
        {
            throw new FinixException(
                (int)response.StatusCode,
                "INVALID_RESPONSE",
                "Finix returned a response body that could not be read.");
        }

        return result;
    }

    private static FinixException BuildException(HttpStatusCode statusCode, string responseBody)
    {
        string? code = null;
        string? message = null;
        string? authorizationId = null;

        if (!string.IsNullOrWhiteSpace(responseBody))
        {
            try
            {
                var errorResponse = JsonSerializer.Deserialize<FinixErrorResponse>(responseBody, JsonOptions);
                var error = errorResponse?.Embedded?.Errors?.FirstOrDefault();
                if (error is not null)
                {
                    code = error.Code;
                    message = error.Message;
                    authorizationId = error.Authorization;

                    if (string.IsNullOrWhiteSpace(authorizationId)
                        && error.Links is not null
                        && error.Links.TryGetValue("authorization", out var link))
                    {
                        authorizationId = LastPathSegment(link.Href);
                    }
                }
            }
            catch (JsonException)
            {
            }
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            message = $"Finix returned HTTP {(int)statusCode} ({statusCode}).";
        }

        return new FinixException((int)statusCode, code, message, authorizationId);
    }

    private static string? LastPathSegment(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        var trimmed = href.TrimEnd('/');
        var index = trimmed.LastIndexOf('/');
        return index >= 0 && index < trimmed.Length - 1 ? trimmed[(index + 1)..] : null;
    }
}
