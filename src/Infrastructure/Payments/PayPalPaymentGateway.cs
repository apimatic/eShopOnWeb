using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// The PayPal implementation of <see cref="IPaymentGateway"/>. Every PayPal interaction goes through
/// the PayPal Server SDK; provider failures are translated into
/// <see cref="PaymentGatewayException"/> / <see cref="PaymentChallengeRequiredException"/> at this
/// boundary. Card details are used only to build the request and are never stored or logged.
/// </summary>
public sealed class PayPalPaymentGateway : IPaymentGateway
{
    // Whole-call budget: RetryOptions.Timeout is per-attempt, so a CancellationToken deadline is the
    // only thing that bounds the entire call (incl. any retries). Linked to the caller's token.
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(60);
    private const int SearchPageSize = 500;
    private const int MaxSearchPagesPerWindow = 200; // hard backstop against an unbounded page loop
    private static readonly TimeSpan MaxSearchWindow = TimeSpan.FromDays(31); // PayPal cap

    // A per-process nonce salts every gateway-generated PayPal-Request-Id (create/authorize/capture/
    // void/reauthorize). Within one process a retried operation reuses its key (idempotent); a fresh
    // process (e.g. the in-memory DB reset that restarts order ids at 1) gets new keys, so it never
    // replays a prior run's cached request. Caller-supplied refund keys are used verbatim and are NOT
    // salted (their whole purpose is cross-request dedupe).
    private static readonly string ProcessNonce = Guid.NewGuid().ToString("N").Substring(0, 8);

    private readonly PayPalServerSdkClient _client;
    private readonly string _currency;

    public PayPalPaymentGateway(PayPalServerSdkClient client, IOptions<PayPalOptions> options)
    {
        _client = client;
        _currency = options.Value.Currency;
    }

    public string Currency => _currency;

    public async Task<GatewayAuthorization> AuthorizeAsync(string eShopOrderReference, decimal amount, string currency,
        CardDetails? card, string? vaultTokenId, string idempotencyKey, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        var token = cts.Token;

        // Direct card processing: the card (or vaulted card) is supplied on the payment source at
        // order creation, with intent=AUTHORIZE. PayPal-Request-Id makes the single-step create idempotent.
        var cardRequest = vaultTokenId is not null
            ? new CardRequest { VaultId = vaultTokenId }
            : BuildCardRequest(card ?? throw new ArgumentException("A card or a saved-card token is required."));

        var orderRequest = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown { CurrencyCode = currency, Value = FormatAmount(amount) },
                    // custom_id carries the eShop reference for reconciliation (surfaces as custom_field in
                    // transaction reports). invoice_id is intentionally omitted: the merchant account requires
                    // it to be unique per transaction, and the eShop order id is not a suitable unique key here.
                    CustomId = eShopOrderReference,
                    Description = $"eShopOnWeb {eShopOrderReference}"
                }
            },
            PaymentSource = new PaymentSource { Card = cardRequest }
        };

        Order created;
        try
        {
            created = await _client.Orders.CreateOrder(
                payPalMockResponse: null, payPalRequestId: $"{idempotencyKey}-{ProcessNonce}", payPalPartnerAttributionId: null,
                payPalClientMetadataId: null, payPalAuthAssertion: null, body: orderRequest,
                prefer: "return=representation", ct: token);
        }
        catch (SdkException<CreateOrderError> ex) { throw TranslateOrderError("create order", ex); }
        catch (JsonException ex) { throw Unreadable("create order", ex); }
        catch (Exception ex) when (IsTransport(ex)) { throw Unreachable("create order", ex); }

        var payPalOrderId = created.Id
            ?? throw new PaymentGatewayException("PayPal did not return an order id.", inner: null);

        // A browser challenge (e.g. 3-D Secure) is not handled — surface it rather than building an approval round-trip.
        EnsureNoChallenge(created.Status, created.Links);

        // With a card on the create request the authorization is usually created in the same step; if not
        // (order merely APPROVED), place the hold with an explicit authorize call (card already attached).
        var authorization = FindAuthorization(created.PurchaseUnits);
        string? description = null;

        if (authorization?.Id is null)
        {
            OrderAuthorizeResponse authResponse;
            try
            {
                authResponse = await _client.Orders.AuthorizeOrder(
                    id: payPalOrderId, payPalMockResponse: null, payPalRequestId: $"{idempotencyKey}-authorize-{ProcessNonce}",
                    payPalClientMetadataId: null, payPalAuthAssertion: null, body: null,
                    prefer: "return=representation", ct: token);
            }
            catch (SdkException<AuthorizeOrderError> ex) { throw TranslateOrderError("authorize order", ex); }
            catch (JsonException ex) { throw Unreadable("authorize order", ex); }
            catch (Exception ex) when (IsTransport(ex)) { throw Unreachable("authorize order", ex); }

            EnsureNoChallenge(authResponse.Status, authResponse.Links);
            authorization = FindAuthorization(authResponse.PurchaseUnits);
            description ??= DescribeCard(authResponse.PaymentSource?.Card);
        }

        if (authorization?.Id is null)
        {
            throw new PaymentGatewayException(
                $"PayPal did not create an authorization for order {payPalOrderId} (status {created.Status?.Value ?? "unknown"}).",
                isClientError: true);
        }

        var expiresAt = ParseDate(authorization.ExpirationTime);
        return new GatewayAuthorization(payPalOrderId, authorization.Id, authorization.Status?.Value ?? "CREATED", expiresAt, description);
    }

    public async Task<GatewayCapture> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);

        CapturedPayment capture;
        try
        {
            capture = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId, payPalMockResponse: null, payPalRequestId: $"{idempotencyKey}-{ProcessNonce}",
                payPalAuthAssertion: null, body: new CaptureRequest { FinalCapture = true },
                prefer: "return=representation", ct: cts.Token);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex) { throw TranslatePaymentError("capture payment", ex.Error, ex); }
        catch (JsonException ex) { throw Unreadable("capture payment", ex); }
        catch (Exception ex) when (IsTransport(ex)) { throw Unreachable("capture payment", ex); }

        var breakdown = capture.SellerReceivableBreakdown;
        var captured = ParseMoney(capture.Amount) ?? ParseMoney(breakdown?.GrossAmount) ?? 0m;
        return new GatewayCapture(
            capture.Id ?? throw new PaymentGatewayException("PayPal did not return a capture id."),
            capture.Status?.Value ?? "COMPLETED",
            captured,
            ParseMoney(breakdown?.PaypalFee),
            ParseMoney(breakdown?.NetAmount));
    }

    public async Task<GatewayReauthorization> ReauthorizeAsync(string authorizationId, string idempotencyKey, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);

        PaymentAuthorization reauth;
        try
        {
            // Omit the amount body → PayPal reauthorizes the original amount.
            reauth = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId, payPalRequestId: $"{idempotencyKey}-{ProcessNonce}", payPalAuthAssertion: null,
                body: null, prefer: "return=representation", ct: cts.Token);
        }
        catch (SdkException<ReauthorizePaymentError> ex) { throw TranslatePaymentError("reauthorize payment", ex.Error, ex); }
        catch (JsonException ex) { throw Unreadable("reauthorize payment", ex); }
        catch (Exception ex) when (IsTransport(ex)) { throw Unreachable("reauthorize payment", ex); }

        return new GatewayReauthorization(
            reauth.Id ?? authorizationId,
            reauth.Status?.Value ?? "CREATED",
            ParseDate(reauth.ExpirationTime));
    }

    public async Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);

        try
        {
            await _client.Payments.VoidPayment(
                authorizationId: authorizationId, payPalMockResponse: null, payPalAuthAssertion: null,
                payPalRequestId: $"{idempotencyKey}-{ProcessNonce}", prefer: "return=minimal", ct: cts.Token);
        }
        catch (SdkException<VoidPaymentError> ex) { throw TranslatePaymentError("void payment", ex.Error, ex); }
        // A successful void returns 204 No Content; the SDK then throws JsonException trying to
        // deserialize the empty body into PaymentAuthorization. That empty 2xx body IS success — a real
        // failure arrives as SdkException<VoidPaymentError> above, not as a JsonException.
        catch (JsonException) { /* void succeeded, no body */ }
        catch (Exception ex) when (IsTransport(ex)) { throw Unreachable("void payment", ex); }
    }

    public async Task<GatewayRefund> RefundAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);

        // null body → full refund; an amount → partial refund. idempotencyKey dedupes at PayPal.
        RefundRequest? body = amount.HasValue
            ? new RefundRequest { Amount = new Money { CurrencyCode = currency, Value = FormatAmount(amount.Value) } }
            : null;

        Refund refund;
        try
        {
            refund = await _client.Payments.RefundCapturedPayment(
                captureId: captureId, payPalMockResponse: null, payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null, body: body, prefer: "return=representation", ct: cts.Token);
        }
        catch (SdkException<RefundCapturedPaymentError> ex) { throw TranslatePaymentError("refund payment", ex.Error, ex); }
        catch (JsonException ex) { throw Unreadable("refund payment", ex); }
        catch (Exception ex) when (IsTransport(ex)) { throw Unreachable("refund payment", ex); }

        return new GatewayRefund(
            refund.Id ?? throw new PaymentGatewayException("PayPal did not return a refund id."),
            refund.Status?.Value ?? "PENDING",
            ParseMoney(refund.Amount) ?? amount ?? 0m);
    }

    public async Task<GatewaySavedCard> VaultCardAsync(CardDetails card, string idempotencyKey, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);

        var body = new PaymentTokenRequest
        {
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    Name = card.CardholderName,
                    BillingAddress = BuildAddress(card.BillingAddress)
                }
            }
        };

        PaymentTokenResponse response;
        try
        {
            response = await _client.Vault.CreatePaymentToken(payPalRequestId: idempotencyKey, body: body, ct: cts.Token);
        }
        catch (SdkException<CreatePaymentTokenError> ex) { throw TranslateVaultError("save card", ex.Error, ex); }
        catch (JsonException ex) { throw Unreadable("save card", ex); }
        catch (Exception ex) when (IsTransport(ex)) { throw Unreachable("save card", ex); }

        var tokenId = response.Id ?? throw new PaymentGatewayException("PayPal did not return a vault token id.");
        var vaultedCard = response.PaymentSource?.Card;
        return new GatewaySavedCard(
            tokenId,
            vaultedCard?.Brand?.Value ?? "UNKNOWN",
            vaultedCard?.LastDigits ?? "----",
            vaultedCard?.Expiry);
    }

    public async Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);

        try
        {
            await _client.Vault.DeletePaymentToken(id: vaultTokenId, ct: cts.Token);
        }
        catch (SdkException<DeletePaymentTokenError> ex) { throw TranslateVaultError("delete card", ex.Error, ex); }
        catch (JsonException ex) { throw Unreadable("delete card", ex); }
        catch (Exception ex) when (IsTransport(ex)) { throw Unreachable("delete card", ex); }
    }

    public async Task<IReadOnlyList<GatewayTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        var token = cts.Token;

        var results = new List<GatewayTransaction>();

        // The whole requested range, in ≤31-day windows (PayPal's cap), each fully paged.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart.Add(MaxSearchWindow);
            if (windowEnd > to) windowEnd = to;

            var page = 1;
            var totalPages = 1;
            do
            {
                SearchResponse response;
                try
                {
                    response = await _client.TransactionSearch.SearchTransactions(
                        startDate: FormatDate(windowStart), endDate: FormatDate(windowEnd),
                        transactionId: null, transactionType: null, transactionStatus: null, transactionAmount: null,
                        transactionCurrency: null, paymentInstrumentType: null, storeId: null, terminalId: null,
                        fields: "transaction_info", balanceAffectingRecordsOnly: "Y",
                        pageSize: SearchPageSize, page: page, ct: token);
                }
                catch (SdkException<RawError> ex) { throw TranslateRaw("reconciliation search", ex.Error, ex); }
                catch (JsonException ex) { throw Unreadable("reconciliation search", ex); }
                catch (Exception ex) when (IsTransport(ex)) { throw Unreachable("reconciliation search", ex); }

                foreach (var detail in response.TransactionDetails ?? new List<TransactionDetails>())
                {
                    var info = detail.TransactionInfo;
                    if (info?.TransactionId is null) continue;
                    results.Add(new GatewayTransaction(
                        info.TransactionId,
                        info.TransactionStatus,
                        ParseMoney(info.TransactionAmount),
                        info.TransactionAmount?.CurrencyCode,
                        ParseDate(info.TransactionInitiationDate),
                        info.InvoiceId,
                        info.CustomField));
                }

                totalPages = response.TotalPages ?? 1;
                page++;
            }
            while (page <= totalPages && page <= MaxSearchPagesPerWindow);

            windowStart = windowEnd;
        }

        return results;
    }

    // ---- mapping helpers -----------------------------------------------------------------------

    private static CardRequest BuildCardRequest(CardDetails card) => new CardRequest
    {
        Number = card.Number,
        Expiry = card.Expiry,
        SecurityCode = card.SecurityCode,
        Name = card.CardholderName,
        BillingAddress = BuildAddress(card.BillingAddress)
    };

    private static Address? BuildAddress(CardBillingAddress? a)
    {
        if (a is null) return null;
        return new Address
        {
            AddressLine1 = a.AddressLine1,
            AddressLine2 = a.AddressLine2,
            AdminArea2 = a.AdminArea2,
            AdminArea1 = a.AdminArea1,
            PostalCode = a.PostalCode,
            CountryCode = string.IsNullOrWhiteSpace(a.CountryCode) ? "US" : a.CountryCode!
        };
    }

    private static string? DescribeCard(CardResponse? card)
    {
        if (card is null) return null;
        var brand = card.Brand?.Value;
        var last = card.LastDigits;
        if (brand is null && last is null) return null;
        return $"{brand ?? "CARD"} ****{last ?? "----"}";
    }

    private static AuthorizationWithAdditionalData? FindAuthorization(IEnumerable<PurchaseUnit>? purchaseUnits) =>
        purchaseUnits?
            .SelectMany(pu => pu.Payments?.Authorizations ?? new List<AuthorizationWithAdditionalData>())
            .FirstOrDefault();

    private static void EnsureNoChallenge(OrderStatus? status, IEnumerable<LinkDescription>? links)
    {
        if (status == OrderStatus.PayerActionRequired || HasPayerActionLink(links))
        {
            throw new PaymentChallengeRequiredException(
                "PayPal requires the shopper to approve this card payment in a browser (3-D Secure / payer action). " +
                "This integration does not support an approval round-trip.");
        }
    }

    private static bool HasPayerActionLink(IEnumerable<LinkDescription>? links) =>
        links?.Any(l => string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(l.Rel, "approve", StringComparison.OrdinalIgnoreCase)) ?? false;

    private static string FormatAmount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal? ParseMoney(Money? money) =>
        money?.Value is { } v && decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static string FormatDate(DateTimeOffset date) =>
        date.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseDate(string? value) =>
        value is not null && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d)
            ? d
            : null;

    // ---- error translation ---------------------------------------------------------------------

    private static PaymentGatewayException TranslateOrderError(string op, SdkException<CreateOrderError> ex)
    {
        ex.Error.TryGetError(out var typed);
        ex.Error.TryGetRawError(out var raw);
        return Translate(op, typed, raw, ex);
    }

    private static PaymentGatewayException TranslateOrderError(string op, SdkException<AuthorizeOrderError> ex)
    {
        ex.Error.TryGetError(out var typed);
        ex.Error.TryGetRawError(out var raw);
        return Translate(op, typed, raw, ex);
    }

    private static PaymentGatewayException TranslatePaymentError(string op, CaptureAuthorizedPaymentError error, Exception ex)
    {
        error.TryGetError(out var typed);
        RawError? raw = null;
        if (typed is null && !error.TryGetNoContent(out raw)) error.TryGetRawError(out raw);
        return Translate(op, typed, raw, ex);
    }

    private static PaymentGatewayException TranslatePaymentError(string op, ReauthorizePaymentError error, Exception ex)
    {
        error.TryGetError(out var typed);
        RawError? raw = null;
        if (typed is null && !error.TryGetNoContent(out raw)) error.TryGetRawError(out raw);
        return Translate(op, typed, raw, ex);
    }

    private static PaymentGatewayException TranslatePaymentError(string op, VoidPaymentError error, Exception ex)
    {
        error.TryGetError(out var typed);
        RawError? raw = null;
        if (typed is null && !error.TryGetNoContent(out raw)) error.TryGetRawError(out raw);
        return Translate(op, typed, raw, ex);
    }

    private static PaymentGatewayException TranslatePaymentError(string op, RefundCapturedPaymentError error, Exception ex)
    {
        error.TryGetError(out var typed);
        RawError? raw = null;
        if (typed is null && !error.TryGetNoContent(out raw)) error.TryGetRawError(out raw);
        return Translate(op, typed, raw, ex);
    }

    private static PaymentGatewayException TranslateVaultError(string op, CreatePaymentTokenError error, Exception ex)
    {
        error.TryGetError(out var typed);
        error.TryGetRawError(out var raw);
        return Translate(op, typed, raw, ex);
    }

    private static PaymentGatewayException TranslateVaultError(string op, DeletePaymentTokenError error, Exception ex)
    {
        error.TryGetError(out var typed);
        error.TryGetRawError(out var raw);
        return Translate(op, typed, raw, ex);
    }

    private static PaymentGatewayException TranslateRaw(string op, RawError raw, Exception ex) =>
        Translate(op, null, raw, ex);

    private static PaymentGatewayException Translate(string op, Error? typed, RawError? raw, Exception inner)
    {
        if (typed is not null)
        {
            var name = typed.Name ?? string.Empty;
            var ours = name.Contains("AUTH", StringComparison.OrdinalIgnoreCase)
                       || name.Contains("PERMISSION", StringComparison.OrdinalIgnoreCase)
                       || name.Contains("NOT_AUTHORIZED", StringComparison.OrdinalIgnoreCase)
                       || name.Contains("INTERNAL", StringComparison.OrdinalIgnoreCase)
                       || name.Contains("RATE", StringComparison.OrdinalIgnoreCase);
            var details = typed.Details is { Count: > 0 }
                ? " Details: " + string.Join("; ", typed.Details.Select(d =>
                    $"{d.Issue}{(d.Field is null ? "" : $" ({d.Field})")}{(d.Description is null ? "" : $": {d.Description}")}"))
                : string.Empty;
            return new PaymentGatewayException(
                $"PayPal rejected the {op}: {typed.Name} — {typed.Message}.{details} (debug_id {typed.DebugId})",
                isClientError: !ours, providerDebugId: typed.DebugId, inner: inner);
        }

        if (raw is not null)
        {
            var status = (int)raw.StatusCode;
            var client = status is >= 400 and < 500 && status is not (401 or 403 or 429);
            return new PaymentGatewayException(
                $"PayPal {op} failed (HTTP {status}). {SafeReadRaw(raw)}",
                isClientError: client, inner: inner);
        }

        return new PaymentGatewayException($"PayPal {op} failed with an unrecognized error.", inner: inner);
    }

    private static string SafeReadRaw(RawError raw)
    {
        try { return raw.ReadAsString(); }
        catch { return string.Empty; }
    }

    private static bool IsTransport(Exception ex) => ex is HttpRequestException or TaskCanceledException or OperationCanceledException;

    private static PaymentGatewayException Unreachable(string op, Exception ex) =>
        new($"PayPal could not be reached for the {op}.", isClientError: false, inner: ex);

    private static PaymentGatewayException Unreadable(string op, Exception ex) =>
        new($"PayPal returned an unreadable response for the {op}.", isClientError: false, inner: ex);
}
