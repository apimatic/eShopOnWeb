using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Finix;

namespace Microsoft.eShopWeb.Infrastructure.Finix;

public class FinixGateway : IFinixGateway
{
    private const string FinixVersion = "2022-02-01";
    private const int PageSize = 100;

    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _authorizationHeader;
    private readonly FinixSettings _settings;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public FinixGateway(HttpClient httpClient, FinixSettings settings)
    {
        _httpClient = httpClient;
        _settings = settings;

        if (string.IsNullOrWhiteSpace(settings.Username)
            || string.IsNullOrWhiteSpace(settings.Password)
            || string.IsNullOrWhiteSpace(settings.MerchantId)
            || string.IsNullOrWhiteSpace(settings.Currency))
        {
            throw new InvalidOperationException(
                "Finix configuration is incomplete. Set Finix:Username, Finix:Password, Finix:MerchantId, and Finix:Currency " +
                "through user-secrets or the FINIX_USERNAME, FINIX_PASSWORD, FINIX_MERCHANT_ID, and FINIX_CURRENCY environment variables.");
        }

        _baseUrl = ResolveBaseUrl(settings);
        _authorizationHeader = "Basic " + Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{settings.Username}:{settings.Password}"));
    }

    public string Currency => _settings.Currency;
    public string MerchantId => _settings.MerchantId;

    public async Task<FinixIdentity> CreateBuyerIdentityAsync(CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["entity"] = new Dictionary<string, object?>()
        };

        return await SendAsync<FinixIdentity>(HttpMethod.Post, "/identities", body, cancellationToken);
    }

    public async Task<FinixPaymentInstrument> CreatePaymentInstrumentAsync(
        string identityId,
        CardDetails card,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["identity"] = identityId,
            ["type"] = "PAYMENT_CARD",
            ["name"] = card.Name,
            ["number"] = card.Number,
            ["expiration_month"] = card.ExpirationMonth,
            ["expiration_year"] = card.ExpirationYear,
            ["security_code"] = card.SecurityCode
        };

        if (card.Address is not null)
        {
            body["address"] = new Dictionary<string, object?>
            {
                ["line1"] = card.Address.Line1,
                ["line2"] = card.Address.Line2,
                ["city"] = card.Address.City,
                ["region"] = card.Address.Region,
                ["postal_code"] = card.Address.PostalCode,
                ["country"] = card.Address.Country
            };
        }

        return await SendAsync<FinixPaymentInstrument>(HttpMethod.Post, "/payment_instruments", body, cancellationToken);
    }

    public async Task<FinixPaymentInstrument> GetPaymentInstrumentAsync(
        string paymentInstrumentId,
        CancellationToken cancellationToken = default)
    {
        return await SendAsync<FinixPaymentInstrument>(
            HttpMethod.Get,
            $"/payment_instruments/{Uri.EscapeDataString(paymentInstrumentId)}",
            null,
            cancellationToken);
    }

    public async Task DisablePaymentInstrumentAsync(
        string paymentInstrumentId,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["enabled"] = false
        };

        await SendAsync<FinixPaymentInstrument>(
            HttpMethod.Put,
            $"/payment_instruments/{Uri.EscapeDataString(paymentInstrumentId)}",
            body,
            cancellationToken);
    }

    public async Task<FinixAuthorization> CreateAuthorizationAsync(
        long amountCents,
        string sourcePaymentInstrumentId,
        string idempotencyId,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["amount"] = amountCents,
            ["currency"] = _settings.Currency,
            ["merchant"] = _settings.MerchantId,
            ["source"] = sourcePaymentInstrumentId,
            ["idempotency_id"] = idempotencyId
        };

        return await SendAsync<FinixAuthorization>(HttpMethod.Post, "/authorizations", body, cancellationToken);
    }

    public async Task<FinixAuthorization?> FindAuthorizationByIdempotencyIdAsync(
        string idempotencyId,
        CancellationToken cancellationToken = default)
    {
        var path = $"/authorizations?idempotency_id={Uri.EscapeDataString(idempotencyId)}&limit=1";
        var result = await SendAsync<FinixAuthorizationList>(HttpMethod.Get, path, null, cancellationToken);
        return result.Authorizations.FirstOrDefault();
    }

    public async Task<FinixAuthorization> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default)
    {
        return await SendAsync<FinixAuthorization>(
            HttpMethod.Get,
            $"/authorizations/{Uri.EscapeDataString(authorizationId)}",
            null,
            cancellationToken);
    }

    public async Task<FinixAuthorization> CaptureAuthorizationAsync(
        string authorizationId,
        long amountCents,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["capture_amount"] = amountCents
        };

        return await SendAsync<FinixAuthorization>(
            HttpMethod.Put,
            $"/authorizations/{Uri.EscapeDataString(authorizationId)}",
            body,
            cancellationToken);
    }

    public async Task<FinixAuthorization> VoidAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["void_me"] = true
        };

        return await SendAsync<FinixAuthorization>(
            HttpMethod.Put,
            $"/authorizations/{Uri.EscapeDataString(authorizationId)}",
            body,
            cancellationToken);
    }

    public async Task<FinixTransfer> CreateRefundAsync(
        string parentTransferId,
        long amountCents,
        string idempotencyId,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["refund_amount"] = amountCents,
            ["idempotency_id"] = idempotencyId
        };

        return await SendAsync<FinixTransfer>(
            HttpMethod.Post,
            $"/transfers/{Uri.EscapeDataString(parentTransferId)}/reversals",
            body,
            cancellationToken);
    }

    public async Task<FinixTransfer?> FindTransferByIdempotencyIdAsync(
        string idempotencyId,
        CancellationToken cancellationToken = default)
    {
        var path = $"/transfers?idempotency_id={Uri.EscapeDataString(idempotencyId)}&limit=1";
        var result = await SendAsync<FinixTransferList>(HttpMethod.Get, path, null, cancellationToken);
        return result.Transfers.FirstOrDefault();
    }

    public async Task<FinixTransfer> GetTransferAsync(
        string transferId,
        CancellationToken cancellationToken = default)
    {
        return await SendAsync<FinixTransfer>(
            HttpMethod.Get,
            $"/transfers/{Uri.EscapeDataString(transferId)}",
            null,
            cancellationToken);
    }

    public async Task<IReadOnlyList<FinixFee>> GetFeesForTransferAsync(
        string transferId,
        CancellationToken cancellationToken = default)
    {
        var fees = new List<FinixFee>();
        string? afterCursor = null;

        for (var page = 0; page < 1000; page++)
        {
            var path = $"/fees?linked_to={Uri.EscapeDataString(transferId)}&limit={PageSize}";
            if (!string.IsNullOrWhiteSpace(afterCursor))
            {
                path += $"&after_cursor={Uri.EscapeDataString(afterCursor)}";
            }

            var result = await SendAsync<FinixFeeList>(HttpMethod.Get, path, null, cancellationToken);
            fees.AddRange(result.Fees);

            afterCursor = result.Page?.NextCursor;
            if (string.IsNullOrWhiteSpace(afterCursor))
            {
                break;
            }
        }

        return fees;
    }

    public async Task<IReadOnlyList<FinixTransfer>> ListTransfersAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var transfers = new List<FinixTransfer>();
        string? afterCursor = null;
        var fromValue = FormatDate(from);
        var toValue = FormatDate(to);

        for (var page = 0; page < 1000; page++)
        {
            var path = $"/transfers?created_at.gte={Uri.EscapeDataString(fromValue)}" +
                       $"&created_at.lte={Uri.EscapeDataString(toValue)}&limit={PageSize}";
            if (!string.IsNullOrWhiteSpace(afterCursor))
            {
                path += $"&after_cursor={Uri.EscapeDataString(afterCursor)}";
            }

            var result = await SendAsync<FinixTransferList>(HttpMethod.Get, path, null, cancellationToken);
            transfers.AddRange(result.Transfers);

            afterCursor = result.Page?.NextCursor;
            if (string.IsNullOrWhiteSpace(afterCursor))
            {
                break;
            }
        }

        return transfers;
    }

    public async Task<IReadOnlyList<FinixAuthorization>> ListAuthorizationsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var authorizations = new List<FinixAuthorization>();
        string? afterCursor = null;
        var fromValue = FormatDate(from);
        var toValue = FormatDate(to);

        for (var page = 0; page < 1000; page++)
        {
            var path = $"/authorizations?created_at.gte={Uri.EscapeDataString(fromValue)}" +
                       $"&created_at.lte={Uri.EscapeDataString(toValue)}&limit={PageSize}";
            if (!string.IsNullOrWhiteSpace(afterCursor))
            {
                path += $"&after_cursor={Uri.EscapeDataString(afterCursor)}";
            }

            var result = await SendAsync<FinixAuthorizationList>(HttpMethod.Get, path, null, cancellationToken);
            authorizations.AddRange(result.Authorizations);

            afterCursor = result.Page?.NextCursor;
            if (string.IsNullOrWhiteSpace(afterCursor))
            {
                break;
            }
        }

        return authorizations;
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, BuildUri(path));
        request.Headers.Authorization = AuthenticationHeaderValue.Parse(_authorizationHeader);
        request.Headers.TryAddWithoutValidation("Finix-Version", FinixVersion);

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, _jsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw BuildException(response.StatusCode, responseText);
        }

        if (string.IsNullOrWhiteSpace(responseText))
        {
            throw new FinixApiException((int)response.StatusCode, "Finix returned an empty response.");
        }

        var result = JsonSerializer.Deserialize<T>(responseText, _jsonOptions);
        if (result is null)
        {
            throw new FinixApiException((int)response.StatusCode, "Finix returned an unreadable response.");
        }

        return result;
    }

    private static FinixApiException BuildException(HttpStatusCode statusCode, string responseText)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<FinixErrorEnvelope>(
                responseText,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var firstError = envelope?.Embedded?.Errors.FirstOrDefault();
            if (firstError is not null)
            {
                return new FinixApiException(
                    (int)statusCode,
                    firstError.Message ?? $"Finix returned HTTP {(int)statusCode}.",
                    firstError.Code,
                    firstError.LogReference);
            }
        }
        catch (JsonException)
        {
        }

        var fallback = string.IsNullOrWhiteSpace(responseText)
            ? $"Finix returned HTTP {(int)statusCode} {statusCode}."
            : $"Finix returned HTTP {(int)statusCode}: {responseText}";

        return new FinixApiException((int)statusCode, fallback);
    }

    private Uri BuildUri(string path)
    {
        return new Uri(_baseUrl + (path.StartsWith('/') ? path : "/" + path), UriKind.Absolute);
    }

    private static string ResolveBaseUrl(FinixSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            return settings.BaseUrl.TrimEnd('/');
        }

        return settings.Environment.ToLowerInvariant() switch
        {
            "sandbox" => "https://finix.sandbox-payments-api.com",
            "live" => "https://finix.live-payments-api.com",
            _ => throw new InvalidOperationException(
                $"Finix:Environment must be 'sandbox' or 'live' (received '{settings.Environment}').")
        };
    }

    private static string FormatDate(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
    }
}
