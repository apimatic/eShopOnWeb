using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.Infrastructure.PayPal.Dto;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Implements IPaymentGateway against PayPal's REST APIs (Orders v2, Payments v2, Vault v3,
/// Transaction Search v1), built strictly to the OpenAPI documents under api-specs/paypal.
/// </summary>
public class PayPalGateway : IPaymentGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string OrderReferenceType = "ODR";

    private readonly HttpClient _httpClient;
    private readonly IPayPalAccessTokenProvider _tokenProvider;
    private readonly IAppLogger<PayPalGateway> _logger;

    public PayPalGateway(HttpClient httpClient, IPayPalAccessTokenProvider tokenProvider, IAppLogger<PayPalGateway> logger)
    {
        _httpClient = httpClient;
        _tokenProvider = tokenProvider;
        _logger = logger;
    }

    public async Task<GatewayAuthorizationResult> AuthorizeAsync(decimal amount, string currency, CardDetails? card,
        string? vaultId, string idempotencyKey, CancellationToken ct = default)
    {
        var cardRequest = vaultId is not null
            ? new CardRequestDto { VaultId = vaultId }
            : ToCardRequestDto(card ?? throw new ArgumentNullException(nameof(card)));

        var createRequest = new OrderCreateRequestDto
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new List<PurchaseUnitRequestDto> { new() { Amount = ToAmountDto(amount, currency) } },
            PaymentSource = new PaymentSourceRequestDto { Card = cardRequest }
        };

        _logger.LogInformation("Creating PayPal order to authorize {0} {1} (idempotency key {2}).", amount, currency, idempotencyKey);
        var order = await SendAsync<OrderResponseDto>(HttpMethod.Post, "/v2/checkout/orders", createRequest,
            idempotencyKey, preferRepresentation: true, ct);

        var authorization = FindAuthorization(order);

        if (authorization is null)
        {
            EnsureNotPayerActionRequired(order.Id, order.Status);

            _logger.LogInformation("PayPal order {0} was created without an authorization; calling /authorize explicitly.", order.Id);
            var authorizeResponse = await SendAsync<OrderResponseDto>(HttpMethod.Post,
                $"/v2/checkout/orders/{order.Id}/authorize", new object(), idempotencyKey, preferRepresentation: true, ct);

            authorization = FindAuthorization(authorizeResponse);
            if (authorization is null)
            {
                EnsureNotPayerActionRequired(order.Id, authorizeResponse.Status);
                throw new PaymentGatewayException(
                    $"PayPal did not return a payment authorization for order {order.Id} (status {authorizeResponse.Status}).");
            }
        }

        return new GatewayAuthorizationResult(
            order.Id,
            authorization.Id,
            authorization.Status,
            ParseAmount(authorization.Amount) ?? amount,
            currency,
            authorization.CreateTime ?? DateTimeOffset.UtcNow,
            authorization.ExpirationTime ?? DateTimeOffset.UtcNow.AddDays(3));
    }

    public async Task<GatewayReauthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, string idempotencyKey, CancellationToken ct = default)
    {
        var request = new ReauthorizeRequestDto { Amount = ToAmountDto(amount, currency) };
        var response = await SendAsync<AuthorizationDetailDto>(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/reauthorize", request, idempotencyKey,
            preferRepresentation: true, ct);

        return new GatewayReauthorizationResult(response.Id, response.Status,
            response.ExpirationTime ?? DateTimeOffset.UtcNow.AddDays(3));
    }

    public async Task<GatewayCaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency,
        bool finalCapture, string idempotencyKey, CancellationToken ct = default)
    {
        var request = new CaptureRequestDto { Amount = ToAmountDto(amount, currency), FinalCapture = finalCapture };
        var response = await SendAsync<CaptureResponseDto>(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/capture", request, idempotencyKey,
            preferRepresentation: true, ct);

        var capturedAmount = ParseAmount(response.SellerReceivableBreakdown?.GrossAmount) ?? ParseAmount(response.Amount) ?? amount;
        var fee = ParseAmount(response.SellerReceivableBreakdown?.PayPalFee);
        var net = ParseAmount(response.SellerReceivableBreakdown?.NetAmount);

        return new GatewayCaptureResult(response.Id, response.Status, capturedAmount, fee, net, currency,
            response.UpdateTime ?? response.CreateTime ?? DateTimeOffset.UtcNow);
    }

    public async Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default)
    {
        await SendNoContentAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void", null,
            idempotencyKey, ct);
    }

    public async Task<GatewayRefundResult> RefundAsync(string captureId, decimal? amount, string currency,
        string? note, string idempotencyKey, CancellationToken ct = default)
    {
        var request = new RefundRequestDto
        {
            Amount = amount.HasValue ? ToAmountDto(amount.Value, currency) : null,
            NoteToPayer = note
        };

        var response = await SendAsync<RefundResponseDto>(HttpMethod.Post,
            $"/v2/payments/captures/{captureId}/refund", request, idempotencyKey, preferRepresentation: true, ct);

        var refundedAmount = ParseAmount(response.Amount) ?? amount ?? 0m;
        return new GatewayRefundResult(response.Id, response.Status, refundedAmount, currency);
    }

    public async Task<GatewaySavedCardResult> SaveCardAsync(string buyerId, CardDetails card, string idempotencyKey,
        CancellationToken ct = default)
    {
        var request = new PaymentTokenRequestDto
        {
            PaymentSource = new PaymentTokenSourceDto { Card = ToCardRequestDto(card) }
        };

        var response = await SendAsync<PaymentTokenResponseDto>(HttpMethod.Post, "/v3/vault/payment-tokens",
            request, idempotencyKey, preferRepresentation: false, ct);

        var cardResponse = response.PaymentSource?.Card
            ?? throw new PaymentGatewayException("PayPal did not return card details for the saved payment token.");

        return new GatewaySavedCardResult(response.Id, cardResponse.Brand ?? "UNKNOWN",
            cardResponse.LastDigits ?? "0000", cardResponse.Expiry ?? "0000-00");
    }

    public async Task DeleteSavedCardAsync(string vaultId, CancellationToken ct = default)
    {
        await SendNoContentAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultId}", null, null, ct);
    }

    public async Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default)
    {
        var results = new List<GatewayTransaction>();
        var windowStart = from;

        // Transaction Search v1 caps a single call's range at 31 days - walk the requested range in windows.
        do
        {
            var windowEnd = windowStart.AddDays(31);
            if (windowEnd > to) windowEnd = to;

            await SearchWindowAsync(windowStart, windowEnd, results, ct);
            windowStart = windowEnd;
        } while (windowStart < to);

        return results;
    }

    private async Task SearchWindowAsync(DateTimeOffset from, DateTimeOffset to, List<GatewayTransaction> results,
        CancellationToken ct)
    {
        var page = 1;
        while (true)
        {
            var path = $"/v1/reporting/transactions?start_date={Uri.EscapeDataString(FormatDate(from))}" +
                       $"&end_date={Uri.EscapeDataString(FormatDate(to))}&fields=all&page_size=500&page={page}";

            var response = await SendAsync<TransactionSearchResponseDto>(HttpMethod.Get, path, null, null,
                preferRepresentation: false, ct);

            foreach (var detail in response.TransactionDetails ?? new List<TransactionDetailDto>())
            {
                var info = detail.TransactionInfo;
                if (info is null) continue;

                results.Add(new GatewayTransaction(
                    info.TransactionId ?? "",
                    info.PayPalReferenceId,
                    info.PayPalReferenceIdType,
                    MapTransactionStatus(info.TransactionStatus),
                    info.TransactionEventCode ?? "",
                    ParseAmount(info.TransactionAmount) ?? 0m,
                    info.TransactionAmount?.CurrencyCode ?? "",
                    info.TransactionInitiationDate ?? DateTimeOffset.MinValue,
                    info.TransactionUpdatedDate ?? DateTimeOffset.MinValue));
            }

            if (response.TotalPages <= page) break;
            page++;
        }
    }

    private static string MapTransactionStatus(string? code) => code switch
    {
        "D" => "DENIED",
        "P" => "PENDING",
        "S" => "SUCCESS",
        "V" => "REVERSED",
        _ => code ?? "UNKNOWN"
    };

    private static string FormatDate(DateTimeOffset dt) =>
        dt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static AuthorizationDto? FindAuthorization(OrderResponseDto order) =>
        order.PurchaseUnits?.SelectMany(pu => pu.Payments?.Authorizations ?? new List<AuthorizationDto>()).FirstOrDefault();

    private static void EnsureNotPayerActionRequired(string orderId, string status)
    {
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentActionRequiredException(
                $"PayPal order {orderId} requires the shopper to complete an approval step in a browser " +
                "(status PAYER_ACTION_REQUIRED). This headless card integration does not support that flow.");
        }
    }

    private static CardRequestDto ToCardRequestDto(CardDetails card) => new()
    {
        Name = card.CardholderName,
        Number = card.Number,
        Expiry = card.ExpiryYearMonth,
        SecurityCode = card.SecurityCode,
        BillingAddress = card.BillingAddress is null ? null : new CardBillingAddressDto
        {
            CountryCode = card.BillingAddress.CountryCode,
            AddressLine1 = card.BillingAddress.AddressLine1,
            AddressLine2 = card.BillingAddress.AddressLine2,
            AdminArea2 = card.BillingAddress.City,
            AdminArea1 = card.BillingAddress.State,
            PostalCode = card.BillingAddress.PostalCode
        }
    };

    private static AmountDto ToAmountDto(decimal amount, string currency) => new()
    {
        CurrencyCode = currency,
        Value = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static decimal? ParseAmount(AmountDto? amount) =>
        amount is null ? null : decimal.Parse(amount.Value, CultureInfo.InvariantCulture);

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, string? idempotencyKey,
        bool preferRepresentation, CancellationToken ct)
    {
        using var response = await SendCoreAsync(method, path, body, idempotencyKey, preferRepresentation, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw BuildException(response.StatusCode, responseBody);

        if (string.IsNullOrWhiteSpace(responseBody))
            throw new PaymentGatewayException($"PayPal returned an empty response for {method} {path}.");

        return JsonSerializer.Deserialize<T>(responseBody, JsonOptions)
            ?? throw new PaymentGatewayException($"PayPal returned an unparsable response for {method} {path}.");
    }

    private async Task SendNoContentAsync(HttpMethod method, string path, object? body, string? idempotencyKey,
        CancellationToken ct)
    {
        using var response = await SendCoreAsync(method, path, body, idempotencyKey, preferRepresentation: false, ct);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            throw BuildException(response.StatusCode, responseBody);
        }
    }

    private async Task<HttpResponseMessage> SendCoreAsync(HttpMethod method, string path, object? body,
        string? idempotencyKey, bool preferRepresentation, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);

        var token = await _tokenProvider.GetAccessTokenAsync(ct);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (idempotencyKey is not null)
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);

        if (preferRepresentation)
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, body.GetType(), JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return await _httpClient.SendAsync(request, ct);
    }

    private static PaymentGatewayException BuildException(HttpStatusCode statusCode, string body)
    {
        PayPalErrorDto? error = null;
        try
        {
            error = JsonSerializer.Deserialize<PayPalErrorDto>(body, JsonOptions);
        }
        catch (JsonException)
        {
            // fall through - PayPal did not return a machine-readable error body.
        }

        var issues = error?.Details?
            .Select(d => d.Issue)
            .Where(i => !string.IsNullOrWhiteSpace(i))
            .Select(i => i!)
            .ToList() ?? new List<string>();

        var message = error?.Message ?? $"PayPal request failed with status {(int)statusCode}.";
        if (issues.Count > 0)
            message += $" Issues: {string.Join(", ", issues)}.";

        return new PaymentGatewayException(message, error?.Name, error?.DebugId, issues);
    }
}
