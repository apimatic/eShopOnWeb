using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// PayPal implementation of <see cref="IPaymentGateway"/>, built directly against the OpenAPI
/// contract in api-specs/paypal (no third-party SDK). Handles authorize/capture/void/refund,
/// card vaulting, and paged, range-chunked transaction reporting.
/// </summary>
public class PayPalGateway : IPaymentGateway
{
    // PayPal reporting caps each request at a 31-day window; we chunk larger ranges.
    private static readonly TimeSpan MaxReportingWindow = TimeSpan.FromDays(31);
    private const int ReportingPageSize = 500;

    private readonly HttpClient _client;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<PayPalGateway> _logger;
    private readonly JsonSerializerOptions _json;

    public PayPalGateway(HttpClient client, IOptions<PayPalSettings> settings, IAppLogger<PayPalGateway> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
        _json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }

    // ---- Authorize (create order with intent=AUTHORIZE) --------------------------------------
    public async Task<GatewayAuthorizationResult> AuthorizeAsync(GatewayAuthorizeRequest request, string idempotencyKey, CancellationToken ct = default)
    {
        var card = request.VaultId is not null
            ? new CardRequest { VaultId = request.VaultId }
            : MapCard(request.Card!);

        var body = new CreateOrderRequest
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new()
            {
                new PurchaseUnitRequest
                {
                    InvoiceId = request.InvoiceId,
                    CustomId = request.InvoiceId,
                    Amount = MoneyOf(request.Amount, request.CurrencyCode)
                }
            },
            PaymentSource = new PaymentSourceRequest { Card = card }
        };

        var order = await SendAsync<OrderResponse>(HttpMethod.Post, "v2/checkout/orders", body, idempotencyKey, preferRepresentation: true, ct);

        // A card that needs shopper approval (e.g. 3-D Secure challenge) comes back not COMPLETED.
        if (!string.Equals(order.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentGatewayException(
                $"PayPal did not complete the authorization (order status: {order.Status}). This typically means the card requires shopper approval (e.g. a 3-D Secure challenge), which is out of scope for this integration.",
                processorIssue: order.Status);
        }

        var authorization = order.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
        if (authorization is null)
            throw new PaymentGatewayException("PayPal completed the order but returned no authorization to hold funds.", processorIssue: "NO_AUTHORIZATION");

        var cardResp = order.PaymentSource?.Card;
        return new GatewayAuthorizationResult
        {
            PayPalOrderId = order.Id,
            AuthorizationId = authorization.Id,
            AuthorizationStatus = authorization.Status,
            ExpiresAt = authorization.ExpirationTime,
            CardBrand = cardResp?.Brand,
            CardLast4 = cardResp?.LastDigits
        };
    }

    // ---- Capture ------------------------------------------------------------------------------
    public async Task<GatewayCaptureResult> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default)
    {
        var capture = await SendAsync<CaptureResponse>(HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/capture",
            new CaptureRequest { FinalCapture = true }, idempotencyKey, preferRepresentation: true, ct);

        var breakdown = capture.SellerReceivableBreakdown;
        return new GatewayCaptureResult
        {
            CaptureId = capture.Id,
            Status = capture.Status,
            GrossAmount = ParseAmount(breakdown?.GrossAmount ?? capture.Amount),
            PayPalFee = breakdown?.PaypalFee is null ? null : ParseAmount(breakdown.PaypalFee),
            NetAmount = breakdown?.NetAmount is null ? null : ParseAmount(breakdown.NetAmount),
            CurrencyCode = (breakdown?.GrossAmount ?? capture.Amount)?.CurrencyCode ?? _settings.Currency
        };
    }

    // ---- Get authorization state --------------------------------------------------------------
    public async Task<GatewayAuthorizationState> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default)
    {
        var auth = await SendAsync<AuthorizationResponse>(HttpMethod.Get,
            $"v2/payments/authorizations/{authorizationId}", null, requestId: null, preferRepresentation: false, ct);

        return new GatewayAuthorizationState
        {
            AuthorizationId = auth.Id,
            Status = auth.Status,
            ExpiresAt = auth.ExpirationTime
        };
    }

    // ---- Reauthorize (renew a stale hold) -----------------------------------------------------
    public async Task<GatewayAuthorizationState> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode, string idempotencyKey, CancellationToken ct = default)
    {
        var auth = await SendAsync<AuthorizationResponse>(HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/reauthorize",
            new ReauthorizeRequest { Amount = MoneyOf(amount, currencyCode) }, idempotencyKey, preferRepresentation: true, ct);

        return new GatewayAuthorizationState
        {
            AuthorizationId = auth.Id,
            Status = auth.Status,
            ExpiresAt = auth.ExpirationTime
        };
    }

    // ---- Void -----------------------------------------------------------------------------------
    public async Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default)
    {
        await SendNoContentAsync(HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/void", null, idempotencyKey, ct);
    }

    // ---- Refund ---------------------------------------------------------------------------------
    public async Task<GatewayRefundResult> RefundAsync(string captureId, decimal? amount, string currencyCode, string? invoiceId, string idempotencyKey, CancellationToken ct = default)
    {
        var body = new RefundRequest
        {
            Amount = amount.HasValue ? MoneyOf(amount.Value, currencyCode) : null,
            InvoiceId = invoiceId
        };

        var refund = await SendAsync<RefundResponse>(HttpMethod.Post,
            $"v2/payments/captures/{captureId}/refund", body, idempotencyKey, preferRepresentation: true, ct);

        return new GatewayRefundResult
        {
            RefundId = refund.Id,
            Status = refund.Status,
            Amount = ParseAmount(refund.Amount),
            CurrencyCode = refund.Amount?.CurrencyCode ?? currencyCode
        };
    }

    // ---- Vault a card ---------------------------------------------------------------------------
    public async Task<GatewayVaultResult> VaultCardAsync(GatewayCardDetails card, string idempotencyKey, CancellationToken ct = default)
    {
        var body = new VaultPaymentTokenRequest
        {
            PaymentSource = new VaultPaymentSource { Card = MapCard(card) }
        };

        var token = await SendAsync<VaultPaymentTokenResponse>(HttpMethod.Post, "v3/vault/payment-tokens", body, idempotencyKey, preferRepresentation: false, ct);

        var cardResp = token.PaymentSource?.Card;
        return new GatewayVaultResult
        {
            VaultId = token.Id,
            PayPalCustomerId = token.Customer?.Id,
            CardBrand = cardResp?.Brand,
            CardLast4 = cardResp?.LastDigits,
            Expiry = cardResp?.Expiry ?? card.Expiry,
            CardholderName = cardResp?.Name ?? card.CardholderName
        };
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default)
    {
        await SendNoContentAsync(HttpMethod.Delete, $"v3/vault/payment-tokens/{vaultId}", null, requestId: null, ct);
    }

    // ---- Transaction reporting (chunked + paged) ----------------------------------------------
    public async Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var results = new List<GatewayTransaction>();

        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart + MaxReportingWindow;
            if (windowEnd > to) windowEnd = to;

            await CollectWindowAsync(windowStart, windowEnd, results, ct);

            windowStart = windowEnd;
        }

        return results;
    }

    private async Task CollectWindowAsync(DateTimeOffset start, DateTimeOffset end, List<GatewayTransaction> sink, CancellationToken ct)
    {
        var page = 1;
        int totalPages;
        do
        {
            var query = $"v1/reporting/transactions?start_date={Iso(start)}&end_date={Iso(end)}&fields=transaction_info&page_size={ReportingPageSize}&page={page}";
            SearchResponse response;
            try
            {
                response = await SendAsync<SearchResponse>(HttpMethod.Get, query, null, requestId: null, preferRepresentation: false, ct);
            }
            catch (PaymentGatewayException ex) when (IsReportingDataNotYetAvailable(ex))
            {
                // PayPal reporting lags live activity by a few hours; a window that is too recent has
                // no data yet. That is an expected result, not a failure — treat the window as empty.
                _logger.LogInformation("PayPal reporting has no data yet for window {0}..{1}; treating as empty.", start, end);
                return;
            }

            foreach (var detail in response.TransactionDetails ?? Enumerable.Empty<TransactionDetail>())
            {
                var info = detail.TransactionInfo;
                if (info is null) continue;
                sink.Add(new GatewayTransaction
                {
                    TransactionId = info.TransactionId ?? string.Empty,
                    InvoiceId = !string.IsNullOrEmpty(info.InvoiceId) ? info.InvoiceId : info.CustomField,
                    Amount = ParseAmount(info.TransactionAmount),
                    CurrencyCode = info.TransactionAmount?.CurrencyCode,
                    Status = info.TransactionStatus,
                    EventCode = info.TransactionEventCode,
                    InitiationDate = info.TransactionInitiationDate
                });
            }

            totalPages = response.TotalPages;
            page++;
        }
        while (page <= totalPages);
    }

    // PayPal returns 404 INVALID_REQUEST "Data for the given start date is not available" when the
    // requested window falls inside its reporting delay (the data simply isn't published yet).
    private static bool IsReportingDataNotYetAvailable(PaymentGatewayException ex) =>
        ex.StatusCode == 404 && (ex.Message.Contains("not available", StringComparison.OrdinalIgnoreCase));

    // ---- helpers --------------------------------------------------------------------------------
    private static CardRequest MapCard(GatewayCardDetails card) => new()
    {
        Number = card.Number,
        Expiry = card.Expiry,
        SecurityCode = card.SecurityCode,
        Name = card.CardholderName,
        BillingAddress = card.BillingAddress is null ? null : new BillingAddress
        {
            AddressLine1 = card.BillingAddress.AddressLine1,
            AddressLine2 = card.BillingAddress.AddressLine2,
            AdminArea2 = card.BillingAddress.AdminArea2,
            AdminArea1 = card.BillingAddress.AdminArea1,
            PostalCode = card.BillingAddress.PostalCode,
            CountryCode = card.BillingAddress.CountryCode
        }
    };

    private Money MoneyOf(decimal amount, string currencyCode) => new()
    {
        CurrencyCode = string.IsNullOrEmpty(currencyCode) ? _settings.Currency : currencyCode,
        Value = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static decimal ParseAmount(Money? money) =>
        money is null || string.IsNullOrEmpty(money.Value)
            ? 0m
            : decimal.Parse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture);

    private static string Iso(DateTimeOffset value) =>
        Uri.EscapeDataString(value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, string? requestId, bool preferRepresentation, CancellationToken ct)
    {
        using var request = BuildRequest(method, path, body, requestId, preferRepresentation);
        using var response = await _client.SendAsync(request, ct);
        await EnsureSuccessAsync(response, method, path, ct);

        var payload = await response.Content.ReadFromJsonAsync<T>(_json, ct);
        if (payload is null)
            throw new PaymentGatewayException($"PayPal returned an empty body for {method} {path}.");
        return payload;
    }

    private async Task SendNoContentAsync(HttpMethod method, string path, object? body, string? requestId, CancellationToken ct)
    {
        using var request = BuildRequest(method, path, body, requestId, preferRepresentation: false);
        using var response = await _client.SendAsync(request, ct);
        await EnsureSuccessAsync(response, method, path, ct);
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string path, object? body, string? requestId, bool preferRepresentation)
    {
        var request = new HttpRequestMessage(method, path);
        if (!string.IsNullOrEmpty(requestId))
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        if (preferRepresentation)
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (body is not null)
            request.Content = JsonContent.Create(body, options: _json);
        return request;
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, HttpMethod method, string path, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;

        var raw = await response.Content.ReadAsStringAsync(ct);
        PayPalErrorResponse? error = null;
        try { error = JsonSerializer.Deserialize<PayPalErrorResponse>(raw, _json); }
        catch { /* body may not be JSON */ }

        var detail = error?.Details?.FirstOrDefault();
        var issue = detail?.Issue ?? error?.Name;
        var message = detail?.Description ?? error?.Message ?? response.ReasonPhrase ?? "Unknown error";
        var friendly = $"PayPal {method} /{path} failed ({(int)response.StatusCode})"
            + (issue is not null ? $" [{issue}]" : string.Empty)
            + $": {message}";

        _logger.LogWarning("PayPal error on {0} {1}: {2} debugId={3}", method, path, friendly, error?.DebugId ?? "-");
        throw new PaymentGatewayException(friendly, processorIssue: issue, statusCode: (int)response.StatusCode);
    }
}
