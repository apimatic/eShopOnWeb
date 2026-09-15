using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Hand-written client for the PayPal REST APIs, built directly against the OpenAPI specs in
/// <c>api-specs/</c> (Checkout Orders v2, Payments v2, Vault Payment Tokens v3, Transaction Search
/// v1). It maps the application's typed requests onto the exact documented request/response shapes,
/// sends idempotency keys via <c>PayPal-Request-Id</c>, and surfaces PayPal's error model as
/// <see cref="PayPalApiException"/>. No third-party PayPal SDK is used.
/// </summary>
public sealed class PayPalClient : IPayPalClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PayPalTokenProvider _tokenProvider;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<PayPalClient> _logger;

    public PayPalClient(IHttpClientFactory httpClientFactory, PayPalTokenProvider tokenProvider,
        PayPalSettings settings, IAppLogger<PayPalClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _tokenProvider = tokenProvider;
        _settings = settings;
        _logger = logger;
    }

    // ---- Checkout Orders v2 ----

    public async Task<PayPalOrderResult> CreateAuthorizeOrderAsync(decimal amount, string currency, string invoiceId,
        string requestId, CancellationToken ct = default)
    {
        var body = new CreateOrderRequest
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new()
                {
                    ReferenceId = invoiceId,
                    InvoiceId = invoiceId,
                    Amount = new Money { CurrencyCode = currency, Value = FormatAmount(amount) }
                }
            }
        };

        var order = await SendAsync<CreateOrderRequest, OrderResponse>(
            HttpMethod.Post, "/v2/checkout/orders", body, requestId, preferRepresentation: false, ct);
        return new PayPalOrderResult(order.Id ?? throw Missing("order id"), order.Status ?? "CREATED");
    }

    public Task<PayPalAuthorizationResult> AuthorizeOrderWithCardAsync(string payPalOrderId, CardDetails card,
        string requestId, CancellationToken ct = default)
    {
        var body = new AuthorizeOrderRequest
        {
            PaymentSource = new PaymentSourceRequest { Card = ToCardRequest(card) }
        };
        return AuthorizeAsync(payPalOrderId, body, requestId, ct);
    }

    public Task<PayPalAuthorizationResult> AuthorizeOrderWithVaultAsync(string payPalOrderId, string vaultTokenId,
        string requestId, CancellationToken ct = default)
    {
        var body = new AuthorizeOrderRequest
        {
            PaymentSource = new PaymentSourceRequest { Card = new CardRequest { VaultId = vaultTokenId } }
        };
        return AuthorizeAsync(payPalOrderId, body, requestId, ct);
    }

    private async Task<PayPalAuthorizationResult> AuthorizeAsync(string payPalOrderId, AuthorizeOrderRequest body,
        string requestId, CancellationToken ct)
    {
        var order = await SendAsync<AuthorizeOrderRequest, OrderResponse>(
            HttpMethod.Post, $"/v2/checkout/orders/{Uri.EscapeDataString(payPalOrderId)}/authorize",
            body, requestId, preferRepresentation: true, ct);

        var auth = order.PurchaseUnits?
            .FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
        var card = order.PaymentSource?.Card;
        var vault = card?.Attributes?.Vault;

        return new PayPalAuthorizationResult(
            OrderStatus: order.Status ?? string.Empty,
            AuthorizationId: auth?.Id,
            Status: auth?.Status,
            Amount: ParseAmount(auth?.Amount?.Value),
            Currency: auth?.Amount?.CurrencyCode,
            ExpiresAt: ParseDate(auth?.ExpirationTime),
            CardBrand: card?.Brand,
            CardLast4: card?.LastDigits,
            VaultTokenId: vault?.Id,
            CustomerId: vault?.Customer?.Id);
    }

    // ---- Payments v2 ----

    public async Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default)
    {
        var auth = await SendAsync<object, AuthorizationResponse>(
            HttpMethod.Get, $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}",
            null, requestId: null, preferRepresentation: false, ct);
        return AuthResultFrom(auth);
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken ct = default)
    {
        var body = new ReauthorizeRequest { Amount = new Money { CurrencyCode = currency, Value = FormatAmount(amount) } };
        var auth = await SendAsync<ReauthorizeRequest, AuthorizationResponse>(
            HttpMethod.Post, $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            body, requestId, preferRepresentation: true, ct);
        return AuthResultFrom(auth);
    }

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken ct = default)
    {
        var body = new CaptureRequest
        {
            Amount = new Money { CurrencyCode = currency, Value = FormatAmount(amount) },
            FinalCapture = true
        };
        var capture = await SendAsync<CaptureRequest, CaptureResponse>(
            HttpMethod.Post, $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            body, requestId, preferRepresentation: true, ct);

        var breakdown = capture.SellerReceivableBreakdown;
        return new PayPalCaptureResult(
            CaptureId: capture.Id ?? throw Missing("capture id"),
            Status: capture.Status ?? "COMPLETED",
            GrossAmount: ParseAmount(breakdown?.GrossAmount?.Value ?? capture.Amount?.Value) ?? amount,
            PayPalFee: ParseAmount(breakdown?.PaypalFee?.Value),
            NetAmount: ParseAmount(breakdown?.NetAmount?.Value),
            Currency: capture.Amount?.CurrencyCode ?? currency);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken ct = default)
    {
        await SendAsync<object, object>(
            HttpMethod.Post, $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void",
            null, requestId, preferRepresentation: false, ct, allowEmptyResponse: true);
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, CancellationToken ct = default)
    {
        var body = new RefundRequest();
        if (amount.HasValue)
        {
            body.Amount = new Money { CurrencyCode = currency, Value = FormatAmount(amount.Value) };
        }
        // Namespace the request id by capture so the same caller key on different captures never collides.
        var requestId = $"refund-{captureId}-{idempotencyKey}";
        var refund = await SendAsync<RefundRequest, RefundResponse>(
            HttpMethod.Post, $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund",
            body, requestId, preferRepresentation: true, ct);

        return new PayPalRefundResult(
            RefundId: refund.Id ?? throw Missing("refund id"),
            Status: refund.Status ?? "COMPLETED",
            Amount: ParseAmount(refund.Amount?.Value) ?? amount ?? 0m,
            Currency: refund.Amount?.CurrencyCode ?? currency);
    }

    // ---- Vault Payment Tokens v3 ----

    public async Task<VaultCardResult> VaultCardAsync(CardDetails card, string? customerId, string requestId,
        CancellationToken ct = default)
    {
        var body = new VaultTokenRequest
        {
            PaymentSource = new VaultPaymentSourceRequest { Card = ToCardRequest(card) }
        };
        if (!string.IsNullOrEmpty(customerId))
        {
            body.Customer = new VaultCustomerRequest { Id = customerId };
        }

        var token = await SendAsync<VaultTokenRequest, VaultTokenResponse>(
            HttpMethod.Post, "/v3/vault/payment-tokens", body, requestId, preferRepresentation: false, ct);

        var respCard = token.PaymentSource?.Card;
        return new VaultCardResult(
            VaultTokenId: token.Id ?? throw Missing("vault token id"),
            CustomerId: token.Customer?.Id,
            Brand: respCard?.Brand,
            Last4: respCard?.LastDigits,
            Expiry: respCard?.Expiry);
    }

    public async Task<IReadOnlyList<VaultedCard>> ListVaultCardsAsync(string customerId, CancellationToken ct = default)
    {
        var path = $"/v3/vault/payment-tokens?customer_id={Uri.EscapeDataString(customerId)}&page_size=50&total_required=true";
        var response = await SendAsync<object, CustomerVaultTokensResponse>(
            HttpMethod.Get, path, null, requestId: null, preferRepresentation: false, ct);

        var result = new List<VaultedCard>();
        foreach (var token in response.PaymentTokens ?? Enumerable.Empty<VaultTokenResponse>())
        {
            var card = token.PaymentSource?.Card;
            if (token.Id is not null && card is not null)
            {
                result.Add(new VaultedCard(token.Id, card.Brand, card.LastDigits, card.Expiry));
            }
        }
        return result;
    }

    public async Task DeleteVaultCardAsync(string vaultTokenId, CancellationToken ct = default)
    {
        await SendAsync<object, object>(
            HttpMethod.Delete, $"/v3/vault/payment-tokens/{Uri.EscapeDataString(vaultTokenId)}",
            null, requestId: null, preferRepresentation: false, ct, allowEmptyResponse: true);
    }

    // ---- Transaction Search v1 ----

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default)
    {
        // PayPal's reporting endpoint rejects any single request whose range exceeds 31 days, so the
        // requested range is split into <=31-day windows; every window is fully paged and the results
        // are de-duplicated by transaction id across window boundaries. This lets a caller ask for an
        // arbitrarily long range while each call honors the documented per-request limit.
        var all = new List<PayPalTransaction>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var windows = 0;

        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart.AddDays(30);
            if (windowEnd > to)
            {
                windowEnd = to;
            }
            windows++;
            await FetchWindowAsync(windowStart, windowEnd, all, seen, ct);
            // Advance to the window boundary; boundary transactions are removed by the id de-dup.
            windowStart = windowEnd;
        }

        _logger.LogInformation("Fetched {0} PayPal transactions across {1} window(s) for {2:o}..{3:o}.",
            all.Count, windows, from, to);
        return all;
    }

    private async Task FetchWindowAsync(DateTimeOffset from, DateTimeOffset to, List<PayPalTransaction> all,
        HashSet<string> seen, CancellationToken ct)
    {
        const int pageSize = 500;
        var startDate = FormatReportingDate(from);
        var endDate = FormatReportingDate(to);

        var page = 1;
        var totalPages = 1;
        do
        {
            var path = "/v1/reporting/transactions"
                + $"?start_date={Uri.EscapeDataString(startDate)}"
                + $"&end_date={Uri.EscapeDataString(endDate)}"
                + "&fields=transaction_info"
                + $"&page_size={pageSize}"
                + $"&page={page}";

            var response = await SendAsync<object, TransactionSearchResponse>(
                HttpMethod.Get, path, null, requestId: null, preferRepresentation: false, ct);

            totalPages = response.TotalPages ?? 1;
            foreach (var detail in response.TransactionDetails ?? Enumerable.Empty<TransactionDetail>())
            {
                var info = detail.TransactionInfo;
                if (info is null) continue;
                // De-duplicate across overlapping window boundaries by transaction id.
                if (info.TransactionId is not null && !seen.Add(info.TransactionId))
                {
                    continue;
                }
                all.Add(new PayPalTransaction(
                    TransactionId: info.TransactionId,
                    EventCode: info.TransactionEventCode,
                    Status: info.TransactionStatus,
                    Amount: ParseAmount(info.TransactionAmount?.Value),
                    Currency: info.TransactionAmount?.CurrencyCode,
                    Fee: ParseAmount(info.FeeAmount?.Value),
                    InvoiceId: info.InvoiceId,
                    InitiationDate: ParseDate(info.TransactionInitiationDate)));
            }
            page++;
        }
        while (page <= totalPages);
    }

    // ---- HTTP plumbing ----

    private async Task<TResponse> SendAsync<TRequest, TResponse>(
        HttpMethod method, string relativePath, TRequest? body, string? requestId,
        bool preferRepresentation, CancellationToken ct, bool allowEmptyResponse = false)
    {
        var client = _httpClientFactory.CreateClient(PayPalHttp.ClientName);
        var token = await _tokenProvider.GetAccessTokenAsync(ct);

        using var request = new HttpRequestMessage(method, relativePath);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        if (!string.IsNullOrEmpty(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }
        if (preferRepresentation)
        {
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        }
        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, PayPalHttp.JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await client.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            var ex = PayPalHttp.BuildException((int)response.StatusCode, responseBody);
            _logger.LogWarning("PayPal {0} {1} failed: HTTP {2} {3} (debug_id {4}).",
                method.Method, relativePath, (int)response.StatusCode, ex.ErrorName ?? ex.Message, ex.DebugId);
            throw ex;
        }

        if (allowEmptyResponse && (response.StatusCode == HttpStatusCode.NoContent || string.IsNullOrWhiteSpace(responseBody)))
        {
            return default!;
        }

        var result = JsonSerializer.Deserialize<TResponse>(responseBody, PayPalHttp.JsonOptions);
        if (result is null)
        {
            throw new PayPalApiException((int)response.StatusCode, "empty_response",
                $"PayPal returned an empty body for {method.Method} {relativePath}.", null, null);
        }
        return result;
    }

    // ---- mapping helpers ----

    private static CardRequest ToCardRequest(CardDetails card) => new()
    {
        Number = card.Number,
        Expiry = card.Expiry,
        SecurityCode = card.SecurityCode,
        Name = card.Name,
        BillingAddress = card.BillingAddress is null ? null : new CardBillingAddressWire
        {
            AddressLine1 = card.BillingAddress.AddressLine1,
            AddressLine2 = card.BillingAddress.AddressLine2,
            AdminArea2 = card.BillingAddress.AdminArea2,
            AdminArea1 = card.BillingAddress.AdminArea1,
            PostalCode = card.BillingAddress.PostalCode,
            CountryCode = card.BillingAddress.CountryCode
        }
    };

    private static PayPalAuthorizationResult AuthResultFrom(AuthorizationResponse auth) => new(
        OrderStatus: string.Empty,
        AuthorizationId: auth.Id,
        Status: auth.Status,
        Amount: ParseAmount(auth.Amount?.Value),
        Currency: auth.Amount?.CurrencyCode,
        ExpiresAt: ParseDate(auth.ExpirationTime),
        CardBrand: null,
        CardLast4: null,
        VaultTokenId: null,
        CustomerId: null);

    private static string FormatAmount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal? ParseAmount(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)
            ? d : null;

    // PayPal reporting expects RFC3339 with a numeric offset; normalize to UTC with a -0000 offset.
    private static string FormatReportingDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) + "-0000";

    private static PayPalApiException Missing(string what) =>
        new(500, "missing_field", $"PayPal response did not include the expected {what}.", null, null);
}
