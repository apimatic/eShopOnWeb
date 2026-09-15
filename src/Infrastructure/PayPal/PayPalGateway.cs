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
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.PayPal.Contracts;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// The only place that speaks HTTP to PayPal. Builds requests exactly as the OpenAPI specs describe
/// (Orders v2, Payments v2, Vault v3, Transaction Search v1), attaches a cached OAuth bearer token, retries
/// transient failures, and parses PayPal's error model.
/// </summary>
public class PayPalGateway : IPayPalGateway
{
    private const int MaxAttempts = 4;

    private readonly HttpClient _http;
    private readonly IPayPalTokenProvider _tokenProvider;
    private readonly IAppLogger<PayPalGateway> _logger;

    public PayPalGateway(HttpClient http, IPayPalTokenProvider tokenProvider, IAppLogger<PayPalGateway> logger)
    {
        _http = http;
        _tokenProvider = tokenProvider;
        _logger = logger;
    }

    // --- Orders v2 -----------------------------------------------------------

    public async Task<AuthorizationResult> AuthorizeOrderAsync(AuthorizeOrderRequest request, CancellationToken cancellationToken = default)
    {
        CardRequestDto card;
        if (!string.IsNullOrEmpty(request.VaultId))
        {
            card = new CardRequestDto { VaultId = request.VaultId };
        }
        else if (request.Card is not null)
        {
            card = MapCard(request.Card);
        }
        else
        {
            throw new ArgumentException("An authorize request must carry either card details or a vault id.");
        }

        var body = new CreateOrderRequestDto
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new List<PurchaseUnitRequestDto>
            {
                new()
                {
                    InvoiceId = request.InvoiceId,
                    CustomId = request.CustomId,
                    Amount = ToMoneyDto(request.Amount),
                    SoftDescriptor = request.SoftDescriptor
                }
            },
            PaymentSource = new OrderPaymentSourceDto { Card = card }
        };

        var headers = new List<(string, string)>
        {
            ("PayPal-Request-Id", request.RequestId),
            ("Prefer", "return=representation")
        };

        var responseBody = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", body, headers, cancellationToken);
        var order = Deserialize<OrderResponseDto>(responseBody);

        // STOP if PayPal wants the shopper to approve in a browser (3-D Secure / payer action).
        var payerActionLink = order.Links?.FirstOrDefault(l => string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase));
        if (string.Equals(order.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase) || payerActionLink is not null)
        {
            throw new PayPalPayerActionRequiredException(order.Id, payerActionLink?.Href);
        }

        var authorization = order.PurchaseUnits?
            .Select(pu => pu.Payments)
            .Where(p => p?.Authorizations is not null)
            .SelectMany(p => p!.Authorizations!)
            .FirstOrDefault();

        if (authorization is null || string.IsNullOrEmpty(authorization.Id))
        {
            throw new PayPalApiException(
                (int)HttpStatusCode.BadGateway, "NO_AUTHORIZATION",
                $"PayPal accepted order {order.Id} (status {order.Status}) but returned no authorization to act on.",
                Array.Empty<string>(), null);
        }

        return new AuthorizationResult(
            order.Id ?? string.Empty,
            authorization.Id!,
            authorization.Status ?? "UNKNOWN",
            ToMoney(authorization.Amount) ?? request.Amount,
            ParseDate(authorization.ExpirationTime));
    }

    // --- Payments v2 ---------------------------------------------------------

    public async Task<AuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        var responseBody = await SendAsync(HttpMethod.Get, $"/v2/payments/authorizations/{authorizationId}", null, null, cancellationToken);
        var auth = Deserialize<AuthorizationDto>(responseBody);
        return new AuthorizationResult(
            string.Empty,
            auth.Id ?? authorizationId,
            auth.Status ?? "UNKNOWN",
            ToMoney(auth.Amount) ?? new PayPalMoney(0m, "USD"),
            ParseDate(auth.ExpirationTime));
    }

    public async Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, PayPalMoney amount, string? requestId, CancellationToken cancellationToken = default)
    {
        var body = new ReauthorizeRequestDto { Amount = ToMoneyDto(amount) };
        var headers = BuildHeaders(requestId, representation: true);
        var responseBody = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/reauthorize", body, headers, cancellationToken);
        var auth = Deserialize<AuthorizationDto>(responseBody);
        return new AuthorizationResult(
            string.Empty,
            auth.Id ?? authorizationId,
            auth.Status ?? "UNKNOWN",
            ToMoney(auth.Amount) ?? amount,
            ParseDate(auth.ExpirationTime));
    }

    public async Task<CaptureResult> CaptureAuthorizationAsync(string authorizationId, PayPalMoney amount, bool finalCapture, string invoiceId, string customId, string? requestId, CancellationToken cancellationToken = default)
    {
        var body = new CaptureRequestDto
        {
            Amount = ToMoneyDto(amount),
            InvoiceId = invoiceId,
            FinalCapture = finalCapture
        };
        var headers = BuildHeaders(requestId, representation: true);
        var responseBody = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture", body, headers, cancellationToken);
        var capture = Deserialize<CaptureDto>(responseBody);

        var breakdown = capture.SellerReceivableBreakdown;
        var gross = ToMoney(breakdown?.GrossAmount) ?? ToMoney(capture.Amount) ?? amount;
        return new CaptureResult(
            capture.Id ?? string.Empty,
            capture.Status ?? "UNKNOWN",
            gross,
            ToMoney(breakdown?.PaypalFee),
            ToMoney(breakdown?.NetAmount));
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string? requestId, CancellationToken cancellationToken = default)
    {
        var headers = BuildHeaders(requestId, representation: false); // Prefer: return=minimal → 204
        await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void", null, headers, cancellationToken);
    }

    public async Task<RefundResult> RefundCaptureAsync(string captureId, PayPalMoney? amount, string invoiceId, string customId, string? requestId, CancellationToken cancellationToken = default)
    {
        var body = new RefundRequestDto
        {
            Amount = amount is null ? null : ToMoneyDto(amount),
            InvoiceId = invoiceId,
            CustomId = customId
        };
        var headers = BuildHeaders(requestId, representation: true);
        var responseBody = await SendAsync(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund", body, headers, cancellationToken);
        var refund = Deserialize<RefundDto>(responseBody);

        return new RefundResult(
            refund.Id ?? string.Empty,
            refund.Status ?? "UNKNOWN",
            ToMoney(refund.Amount) ?? amount ?? new PayPalMoney(0m, "USD"),
            ToMoney(refund.SellerPayableBreakdown?.TotalRefundedAmount));
    }

    // --- Vault v3 ------------------------------------------------------------

    public async Task<VaultedCardResult> VaultCardAsync(VaultCardRequest request, string? requestId, CancellationToken cancellationToken = default)
    {
        var body = new CreatePaymentTokenRequestDto
        {
            PaymentSource = new VaultPaymentSourceDto
            {
                Card = new VaultCardDto
                {
                    Number = request.Card.Number,
                    Expiry = request.Card.ExpiryYearMonth,
                    SecurityCode = request.Card.SecurityCode,
                    Name = request.Card.CardholderName,
                    BillingAddress = MapBillingAddress(request.Card)
                }
            },
            Customer = new VaultCustomerDto { Id = request.CustomerId }
        };

        var headers = requestId is null ? null : new List<(string, string)> { ("PayPal-Request-Id", requestId) };
        var responseBody = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", body, headers, cancellationToken);
        var token = Deserialize<PaymentTokenResponseDto>(responseBody);

        if (string.IsNullOrEmpty(token.Id))
        {
            throw new PayPalApiException((int)HttpStatusCode.BadGateway, "NO_VAULT_TOKEN",
                "PayPal did not return a vault token id when saving the card.", Array.Empty<string>(), null);
        }

        var responseCard = token.PaymentSource?.Card;
        return new VaultedCardResult(
            token.Id!,
            token.Customer?.Id ?? request.CustomerId,
            responseCard?.Brand ?? "UNKNOWN",
            responseCard?.LastDigits ?? string.Empty,
            responseCard?.Expiry ?? request.Card.ExpiryYearMonth,
            responseCard?.Name ?? request.Card.CardholderName);
    }

    public async Task DeleteVaultedCardAsync(string tokenId, CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{tokenId}", null, null, cancellationToken);
    }

    // --- Transaction Search v1 ----------------------------------------------

    public async Task<TransactionSearchPage> SearchTransactionsAsync(TransactionSearchQuery query, CancellationToken cancellationToken = default)
    {
        var pageSize = Math.Clamp(query.PageSize, 1, 500);
        var page = Math.Max(query.Page, 1);
        var qs = new StringBuilder("/v1/reporting/transactions?");
        qs.Append("start_date=").Append(Uri.EscapeDataString(FormatReportingDate(query.StartDate)));
        qs.Append("&end_date=").Append(Uri.EscapeDataString(FormatReportingDate(query.EndDate)));
        qs.Append("&fields=transaction_info");
        qs.Append("&balance_affecting_records_only=N");
        qs.Append("&page_size=").Append(pageSize);
        qs.Append("&page=").Append(page);

        var responseBody = await SendAsync(HttpMethod.Get, qs.ToString(), null, null, cancellationToken);
        var result = Deserialize<SearchResponseDto>(responseBody);

        var transactions = (result.TransactionDetails ?? new List<TransactionDetailDto>())
            .Where(d => d.TransactionInfo is not null)
            .Select(d => MapTransaction(d.TransactionInfo!))
            .ToList();

        return new TransactionSearchPage(transactions, result.Page, result.TotalPages, result.TotalItems);
    }

    // --- HTTP core -----------------------------------------------------------

    private async Task<string> SendAsync(HttpMethod method, string path, object? body, IEnumerable<(string name, string value)>? headers, CancellationToken cancellationToken)
    {
        // Only idempotent reads (GET) are safe to auto-retry on transient/network failures. Financial writes
        // (authorize/capture/refund/void) are NOT retried blindly: re-sending could double-charge, and PayPal
        // rejects a re-sent PayPal-Request-Id as DUPLICATE_REQUEST_ID. A 401 is always safe to retry because the
        // request was rejected before processing.
        var canRetryTransient = method == HttpMethod.Get;

        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null)
            {
                var json = JsonSerializer.Serialize(body, body.GetType(), PayPalJson.Options);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }
            if (headers is not null)
            {
                foreach (var (name, value) in headers)
                {
                    request.Headers.TryAddWithoutValidation(name, value);
                }
            }

            var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                if (canRetryTransient && attempt < MaxAttempts)
                {
                    _logger.LogWarning($"PayPal {method} {path} network error (attempt {attempt}): {ex.Message}. Retrying.");
                    await BackoffAsync(attempt, cancellationToken);
                    continue;
                }
                throw new PayPalApiException((int)HttpStatusCode.BadGateway, "NETWORK_ERROR",
                    $"Network error calling PayPal: {ex.Message}", Array.Empty<string>(), null);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (canRetryTransient && attempt < MaxAttempts)
                {
                    _logger.LogWarning($"PayPal {method} {path} timed out (attempt {attempt}). Retrying.");
                    await BackoffAsync(attempt, cancellationToken);
                    continue;
                }
                throw new PayPalApiException((int)HttpStatusCode.GatewayTimeout, "TIMEOUT",
                    "PayPal request timed out.", Array.Empty<string>(), null);
            }

            using (response)
            {
                var content = response.Content is null ? string.Empty : await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    return content;
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized && attempt < MaxAttempts)
                {
                    _logger.LogWarning($"PayPal {method} {path} returned 401; refreshing token and retrying.");
                    _tokenProvider.Invalidate();
                    continue;
                }

                if (canRetryTransient && IsTransient(response.StatusCode) && attempt < MaxAttempts)
                {
                    _logger.LogWarning($"PayPal {method} {path} returned {(int)response.StatusCode} (attempt {attempt}). Retrying.");
                    await BackoffAsync(attempt, cancellationToken);
                    continue;
                }

                throw PayPalErrorParser.ToException((int)response.StatusCode, content);
            }
        }
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status == HttpStatusCode.TooManyRequests ||
        status == HttpStatusCode.InternalServerError ||
        status == HttpStatusCode.BadGateway ||
        status == HttpStatusCode.ServiceUnavailable ||
        status == HttpStatusCode.GatewayTimeout;

    private static async Task BackoffAsync(int attempt, CancellationToken cancellationToken)
    {
        // Exponential backoff with a little deterministic jitter derived from the attempt number.
        var delayMs = (int)(200 * Math.Pow(2, attempt - 1)) + (attempt * 37);
        await Task.Delay(delayMs, cancellationToken);
    }

    private static T Deserialize<T>(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new PayPalApiException((int)HttpStatusCode.BadGateway, "EMPTY_RESPONSE",
                "PayPal returned an empty response where a body was expected.", Array.Empty<string>(), null);
        }
        var value = JsonSerializer.Deserialize<T>(body, PayPalJson.Options);
        if (value is null)
        {
            throw new PayPalApiException((int)HttpStatusCode.BadGateway, "UNPARSEABLE_RESPONSE",
                "PayPal returned a response that could not be parsed.", Array.Empty<string>(), null);
        }
        return value;
    }

    private static List<(string, string)> BuildHeaders(string? requestId, bool representation)
    {
        var headers = new List<(string, string)>
        {
            ("Prefer", representation ? "return=representation" : "return=minimal")
        };
        if (!string.IsNullOrEmpty(requestId))
        {
            headers.Add(("PayPal-Request-Id", requestId!));
        }
        return headers;
    }

    // --- mapping -------------------------------------------------------------

    private static CardRequestDto MapCard(CardDetails card) => new()
    {
        Number = card.Number,
        Expiry = card.ExpiryYearMonth,
        SecurityCode = card.SecurityCode,
        Name = card.CardholderName,
        BillingAddress = MapBillingAddress(card)
    };

    private static CardBillingAddressDto MapBillingAddress(CardDetails card) => new()
    {
        AddressLine1 = card.BillingAddressLine1,
        AddressLine2 = card.BillingAddressLine2,
        AdminArea1 = card.BillingAdminArea1,
        AdminArea2 = card.BillingAdminArea2,
        PostalCode = card.BillingPostalCode,
        CountryCode = card.BillingCountryCode
    };

    private static MoneyDto ToMoneyDto(PayPalMoney money) => new()
    {
        CurrencyCode = money.CurrencyCode,
        Value = PayPalMoneyFormatter.Format(money)
    };

    private static PayPalMoney? ToMoney(MoneyDto? dto)
    {
        if (dto is null || string.IsNullOrEmpty(dto.Value) || string.IsNullOrEmpty(dto.CurrencyCode))
        {
            return null;
        }
        return new PayPalMoney(PayPalMoneyFormatter.Parse(dto.Value!), dto.CurrencyCode!);
    }

    private static PayPalTransaction MapTransaction(TransactionInfoDto info) => new()
    {
        TransactionId = info.TransactionId ?? string.Empty,
        EventCode = info.TransactionEventCode,
        Status = info.TransactionStatus,
        InitiationDate = ParseDate(info.TransactionInitiationDate),
        UpdatedDate = ParseDate(info.TransactionUpdatedDate),
        Amount = ToMoney(info.TransactionAmount),
        FeeAmount = ToMoney(info.FeeAmount),
        InvoiceId = info.InvoiceId,
        CustomField = info.CustomField,
        ReferenceId = info.PaypalReferenceId
    };

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }

    private static string FormatReportingDate(DateTimeOffset value) =>
        // RFC3339 with required seconds and timezone offset, e.g. 2026-08-11T00:00:00+00:00.
        value.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
}
