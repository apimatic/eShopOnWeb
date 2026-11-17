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
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Finix;

/// <summary>
/// Plain HTTP client for the Finix API. The contract (base URLs, Finix-Version
/// header, request/response fields and pagination) was verified against the
/// official Finix OpenAPI description.
/// </summary>
public class FinixApiClient : IFinixPaymentGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private readonly HttpClient _httpClient;
    private readonly FinixSettings _settings;

    public FinixApiClient(HttpClient httpClient, IOptions<FinixSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
    }

    public async Task<FinixIdentityResponse> CreateBuyerIdentityAsync(CancellationToken cancellationToken)
    {
        var request = new FinixCreateIdentityRequest();
        return await SendAsync<FinixIdentityResponse>(
            HttpMethod.Post,
            "/identities",
            request,
            cancellationToken);
    }

    public async Task<FinixPaymentInstrumentResponse> CreatePaymentInstrumentAsync(
        string identityId,
        CardRequest card,
        string? ownerTag,
        CancellationToken cancellationToken)
    {
        var request = new FinixCreatePaymentInstrumentRequest
        {
            Identity = identityId,
            Name = card.Name,
            Number = NormalizeCardNumber(card.Number),
            ExpirationMonth = card.ExpirationMonth,
            ExpirationYear = card.ExpirationYear,
            SecurityCode = card.SecurityCode,
            Address = ToFinixAddress(card.BillingAddress),
            Tags = string.IsNullOrWhiteSpace(ownerTag)
                ? null
                : new Dictionary<string, string> { ["eshop_owner"] = ownerTag! }
        };

        return await SendAsync<FinixPaymentInstrumentResponse>(
            HttpMethod.Post,
            "/payment_instruments",
            request,
            cancellationToken);
    }

    public async Task<FinixPaymentInstrumentResponse> GetPaymentInstrumentAsync(
        string paymentInstrumentId,
        CancellationToken cancellationToken)
    {
        return await SendAsync<FinixPaymentInstrumentResponse>(
            HttpMethod.Get,
            $"/payment_instruments/{Uri.EscapeDataString(paymentInstrumentId)}",
            null,
            cancellationToken);
    }

    public async Task DisablePaymentInstrumentAsync(string paymentInstrumentId, CancellationToken cancellationToken)
    {
        var request = new { enabled = false };
        await SendAsync<JsonElement>(
            HttpMethod.Put,
            $"/payment_instruments/{Uri.EscapeDataString(paymentInstrumentId)}",
            request,
            cancellationToken);
    }

    public async Task<FinixAuthorizationResponse> CreateAuthorizationAsync(
        string sourcePaymentInstrumentId,
        int amountCents,
        string idempotencyId,
        int orderId,
        CancellationToken cancellationToken)
    {
        var request = new FinixCreateAuthorizationRequest
        {
            Amount = amountCents,
            Currency = _settings.Currency,
            Merchant = _settings.MerchantId,
            Source = sourcePaymentInstrumentId,
            IdempotencyId = idempotencyId,
            Tags = new Dictionary<string, string>
            {
                ["eshop_order_id"] = orderId.ToString(CultureInfo.InvariantCulture)
            }
        };

        return await SendAsync<FinixAuthorizationResponse>(
            HttpMethod.Post,
            "/authorizations",
            request,
            cancellationToken);
    }

    public async Task<FinixAuthorizationResponse> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken)
    {
        return await SendAsync<FinixAuthorizationResponse>(
            HttpMethod.Get,
            $"/authorizations/{Uri.EscapeDataString(authorizationId)}",
            null,
            cancellationToken);
    }

    public async Task<FinixAuthorizationResponse?> FindAuthorizationByIdempotencyIdAsync(
        string idempotencyId,
        CancellationToken cancellationToken)
    {
        var url = "/authorizations?idempotency_id=" + Uri.EscapeDataString(idempotencyId) + "&limit=100";
        var page = await SendAsync<FinixListResponse<FinixAuthorizationResponse>>(
            HttpMethod.Get,
            url,
            null,
            cancellationToken);

        return page.Embedded?.Authorizations is { Count: > 0 } authorizations
            ? authorizations[0]
            : null;
    }

    public async Task<FinixAuthorizationResponse> CaptureAuthorizationAsync(
        string authorizationId,
        int amountCents,
        CancellationToken cancellationToken)
    {
        var request = new FinixCaptureRequest { CaptureAmount = amountCents };
        return await SendAsync<FinixAuthorizationResponse>(
            HttpMethod.Put,
            $"/authorizations/{Uri.EscapeDataString(authorizationId)}",
            request,
            cancellationToken);
    }

    public async Task<FinixAuthorizationResponse> VoidAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken)
    {
        var request = new FinixVoidRequest();
        return await SendAsync<FinixAuthorizationResponse>(
            HttpMethod.Put,
            $"/authorizations/{Uri.EscapeDataString(authorizationId)}",
            request,
            cancellationToken);
    }

    public async Task<FinixTransferResponse> GetTransferAsync(
        string transferId,
        CancellationToken cancellationToken)
    {
        return await SendAsync<FinixTransferResponse>(
            HttpMethod.Get,
            $"/transfers/{Uri.EscapeDataString(transferId)}",
            null,
            cancellationToken);
    }

    public async Task<FinixTransferResponse?> FindTransferByIdempotencyIdAsync(
        string idempotencyId,
        CancellationToken cancellationToken)
    {
        var url = "/transfers?idempotency_id=" + Uri.EscapeDataString(idempotencyId) + "&limit=100";
        var page = await SendAsync<FinixListResponse<FinixTransferResponse>>(
            HttpMethod.Get,
            url,
            null,
            cancellationToken);

        return page.Embedded?.Transfers is { Count: > 0 } transfers
            ? transfers[0]
            : null;
    }

    public async Task<FinixTransferResponse> CreateRefundAsync(
        string transferId,
        int amountCents,
        string idempotencyId,
        CancellationToken cancellationToken)
    {
        var request = new FinixReversalRequest
        {
            RefundAmount = amountCents,
            IdempotencyId = idempotencyId
        };

        return await SendAsync<FinixTransferResponse>(
            HttpMethod.Post,
            $"/transfers/{Uri.EscapeDataString(transferId)}/reversals",
            request,
            cancellationToken);
    }

    public async Task<IReadOnlyList<FinixTransferResponse>> ListTransfersAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var results = new List<FinixTransferResponse>();
        string? cursor = null;
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);

        do
        {
            var url = "/transfers" +
                      "?created_at.gte=" + Uri.EscapeDataString(FormatFinixDate(from)) +
                      "&created_at.lte=" + Uri.EscapeDataString(FormatFinixDate(to)) +
                      "&limit=100";

            if (!string.IsNullOrWhiteSpace(cursor))
            {
                url += "&after_cursor=" + Uri.EscapeDataString(cursor!);
            }

            var page = await SendAsync<FinixListResponse<FinixTransferResponse>>(
                HttpMethod.Get,
                url,
                null,
                cancellationToken);

            if (page.Embedded?.Transfers is { Count: > 0 })
            {
                results.AddRange(page.Embedded.Transfers);
            }

            var next = page.Page?.NextCursor;
            if (string.IsNullOrWhiteSpace(next) || !seenCursors.Add(next!))
            {
                break;
            }

            cursor = next;
        }
        while (true);

        return results;
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, BuildUrl(path));
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.Username}:{_settings.Password}")));
        request.Headers.Add("Finix-Version", FinixSettings.ApiVersion);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            await ThrowFinixErrorAsync(response, cancellationToken);
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
        {
            return default!;
        }

        var result = JsonSerializer.Deserialize<T>(content, JsonOptions);
        if (result is null)
        {
            throw new FinixApiException(
                (int)response.StatusCode,
                "Finix returned an empty or unreadable response.",
                "INVALID_RESPONSE");
        }

        return result;
    }

    private async Task ThrowFinixErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        string? code = null;
        string? message = null;
        string? logReference = null;

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("_embedded", out var embedded) &&
                embedded.TryGetProperty("errors", out var errors) &&
                errors.ValueKind == JsonValueKind.Array &&
                errors.GetArrayLength() > 0)
            {
                var first = errors[0];
                code = first.TryGetProperty("code", out var codeValue) ? codeValue.GetString() : null;
                message = first.TryGetProperty("message", out var messageValue) ? messageValue.GetString() : null;
                logReference = first.TryGetProperty("logref", out var logrefValue) ? logrefValue.GetString() : null;
            }
        }
        catch (JsonException)
        {
            // Fall through to the generic message below.
        }

        message ??= $"Finix request failed with HTTP {(int)response.StatusCode}.";
        if (!string.IsNullOrWhiteSpace(logReference))
        {
            message += $" (Finix log reference: {logReference})";
        }

        throw new FinixApiException((int)response.StatusCode, message, code, logReference);
    }

    private string BuildUrl(string path)
    {
        var baseUrl = _settings.ResolveBaseUrl();
        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        return baseUrl + path;
    }

    private static string FormatFinixDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string NormalizeCardNumber(string number) =>
        (number ?? string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty).Trim();

    private static FinixAddress? ToFinixAddress(ShippingAddressRequest? address)
    {
        if (address is null)
        {
            return null;
        }

        return new FinixAddress
        {
            Line1 = address.Street,
            City = address.City,
            Region = address.State,
            PostalCode = address.ZipCode,
            Country = address.Country
        };
    }
}
