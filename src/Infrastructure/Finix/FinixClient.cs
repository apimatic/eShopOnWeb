using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Finix;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Finix;

/// <summary>
/// Hand-written Finix client built directly against finix-spec/finix-openapi.yaml.
/// The client only talks to the resources described by that specification.
/// </summary>
public class FinixClient : IFinixClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly FinixOptions _options;
    private readonly ILogger<FinixClient> _logger;

    public FinixClient(HttpClient httpClient, IOptions<FinixOptions> options, ILogger<FinixClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public Task<FinixIdentity> CreateBuyerIdentityAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var email = buyerId.Contains('@') ? buyerId : $"{SanitizeName(buyerId)}@example.com";
        var firstName = buyerId.Contains('@')
            ? buyerId[..buyerId.IndexOf('@')]
            : buyerId;

        var request = new FinixCreateIdentityRequest
        {
            Type = "PERSONAL",
            Entity = new FinixEntity
            {
                FirstName = SanitizeName(firstName),
                LastName = "Shopper",
                Email = email
            }
        };

        return SendAsync<FinixIdentity>(HttpMethod.Post, "identities", request, cancellationToken);
    }

    public Task<FinixPaymentInstrument> CreatePaymentInstrumentAsync(
        FinixCreatePaymentInstrumentRequest request,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<FinixPaymentInstrument>(
            HttpMethod.Post,
            "payment_instruments",
            request,
            cancellationToken);
    }

    public Task<FinixPaymentInstrument> SetPaymentInstrumentEnabledAsync(
        string paymentInstrumentId,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var request = new { enabled };
        return SendAsync<FinixPaymentInstrument>(
            HttpMethod.Put,
            $"payment_instruments/{Uri.EscapeDataString(paymentInstrumentId)}",
            request,
            cancellationToken);
    }

    public Task<FinixAuthorization> CreateAuthorizationAsync(
        FinixCreateAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<FinixAuthorization>(
            HttpMethod.Post,
            "authorizations",
            request,
            cancellationToken);
    }

    public Task<FinixAuthorization> CaptureAuthorizationAsync(
        string authorizationId,
        long captureAmountCents,
        CancellationToken cancellationToken = default)
    {
        var request = new FinixCaptureAuthorizationRequest { CaptureAmount = captureAmountCents };
        return SendAsync<FinixAuthorization>(
            HttpMethod.Put,
            $"authorizations/{Uri.EscapeDataString(authorizationId)}",
            request,
            cancellationToken);
    }

    public Task<FinixAuthorization> VoidAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default)
    {
        var request = new FinixVoidAuthorizationRequest { VoidMe = true };
        return SendAsync<FinixAuthorization>(
            HttpMethod.Put,
            $"authorizations/{Uri.EscapeDataString(authorizationId)}",
            request,
            cancellationToken);
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

    public Task<FinixTransfer> CreateRefundAsync(
        string transferId,
        long refundAmountCents,
        CancellationToken cancellationToken = default)
    {
        var request = new FinixCreateRefundRequest { RefundAmount = refundAmountCents };
        return SendAsync<FinixTransfer>(
            HttpMethod.Post,
            $"transfers/{Uri.EscapeDataString(transferId)}/reversals",
            request,
            cancellationToken);
    }

    public async Task<IReadOnlyList<FinixTransfer>> ListTransfersAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        return await ListAllAsync(
            "transfers",
            from,
            to,
            (FinixTransferListResponse response) => response.Embedded?.Transfers,
            (FinixTransferListResponse response) => response.Page?.NextCursor,
            cancellationToken);
    }

    public async Task<IReadOnlyList<FinixAuthorization>> ListAuthorizationsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        return await ListAllAsync(
            "authorizations",
            from,
            to,
            (FinixAuthorizationListResponse response) => response.Embedded?.Authorizations,
            (FinixAuthorizationListResponse response) => response.Page?.NextCursor,
            cancellationToken);
    }

    private async Task<List<TItem>> ListAllAsync<TItem, TResponse>(
        string resource,
        DateTimeOffset from,
        DateTimeOffset to,
        Func<TResponse, List<TItem>?> itemsSelector,
        Func<TResponse, string?> cursorSelector,
        CancellationToken cancellationToken)
    {
        var results = new List<TItem>();
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;

        for (var page = 0; page < 1000; page++)
        {
            var url = $"{resource}?{BuildListQuery(from, to, cursor)}";
            var response = await SendAsync<TResponse>(HttpMethod.Get, url, null, cancellationToken);
            var items = itemsSelector(response);
            if (items is { Count: > 0 })
            {
                results.AddRange(items);
            }

            var nextCursor = cursorSelector(response);
            if (string.IsNullOrWhiteSpace(nextCursor) || !seenCursors.Add(nextCursor))
            {
                break;
            }

            cursor = nextCursor;
        }

        return results;
    }

    private string BuildListQuery(DateTimeOffset from, DateTimeOffset to, string? cursor)
    {
        var query = new StringBuilder();
        AppendQuery(query, "created_at.gte", FormatDate(from));
        AppendQuery(query, "created_at.lte", FormatDate(to));
        AppendQuery(query, "merchant", _options.MerchantId);
        AppendQuery(query, "limit", "100");
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            AppendQuery(query, "after_cursor", cursor);
        }
        return query.ToString();
    }

    private static void AppendQuery(StringBuilder query, string key, string value)
    {
        if (query.Length > 0)
        {
            query.Append('&');
        }
        query.Append(key).Append('=').Append(Uri.EscapeDataString(value));
    }

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string relativeUrl,
        object? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, relativeUrl);
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
            var exception = BuildException(response, responseBody);
            _logger.LogWarning(
                "Finix {Method} {Path} failed with HTTP {StatusCode} ({Code}, logref {LogRef}): {Message}",
                method.Method,
                relativeUrl.Split('?')[0],
                exception.StatusCode,
                exception.Code,
                exception.LogRef,
                exception.Message);
            throw exception;
        }

        if (string.IsNullOrWhiteSpace(responseBody))
        {
            throw new FinixApiException(
                (int)response.StatusCode,
                null,
                null,
                $"Finix returned an empty response for {method.Method} {relativeUrl}.");
        }

        var result = JsonSerializer.Deserialize<T>(responseBody, JsonOptions);
        if (result is null)
        {
            throw new FinixApiException(
                (int)response.StatusCode,
                null,
                null,
                $"Finix returned an unreadable response for {method.Method} {relativeUrl}.");
        }

        return result;
    }

    private static FinixApiException BuildException(HttpResponseMessage response, string responseBody)
    {
        string? code = null;
        string? logRef = null;
        string? message = null;

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.TryGetProperty("_embedded", out var embedded) &&
                embedded.TryGetProperty("errors", out var errors) &&
                errors.ValueKind == JsonValueKind.Array)
            {
                foreach (var error in errors.EnumerateArray())
                {
                    if (error.TryGetProperty("code", out var codeElement))
                    {
                        code = codeElement.GetString();
                    }
                    if (error.TryGetProperty("logref", out var logRefElement))
                    {
                        logRef = logRefElement.GetString();
                    }
                    if (error.TryGetProperty("message", out var messageElement))
                    {
                        message = messageElement.GetString();
                    }
                    break;
                }
            }
        }
        catch (JsonException)
        {
            // The error body is not the documented Finix error envelope; fall back to the HTTP status.
        }

        message ??= $"Finix request failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).";
        return new FinixApiException((int)response.StatusCode, code, logRef, message, responseBody);
    }

    private static string SanitizeName(string value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return "Shopper";
        }
        return trimmed.Length <= 50 ? trimmed : trimmed[..50];
    }
}
