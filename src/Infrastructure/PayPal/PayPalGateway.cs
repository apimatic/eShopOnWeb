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
using Microsoft.Extensions.Logging;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// The one class that talks to PayPal via the PayPal Server SDK. Every SDK call is wrapped so that no SDK
/// exception, JSON parse failure, or transport fault escapes as anything other than a
/// <see cref="PaymentException"/>. A per-call cancellation deadline bounds the whole call (the SDK's own
/// Timeout is per-attempt only).
/// </summary>
public sealed class PayPalGateway : IPayPalGateway
{
    private readonly PayPalServerSdkClient _client;
    private readonly ILogger<PayPalGateway> _logger;
    private readonly string _currency;
    private readonly TimeSpan _callBudget = TimeSpan.FromSeconds(60);

    // Reconciliation page-walk backstop: covers a very large range but never loops unbounded.
    private const int MaxReconciliationPages = 500;
    private const int ReconciliationPageSize = 100;

    public PayPalGateway(PayPalServerSdkClient client, ILogger<PayPalGateway> logger, string currency)
    {
        _client = client;
        _logger = logger;
        _currency = currency;
    }

    // ── Authorize (create order with card/vault + AUTHORIZE intent) ──────────
    public async Task<AuthorizationResult> AuthorizeAsync(AuthorizeGatewayRequest request, CancellationToken ct)
    {
        using var cts = Deadline(ct);
        var token = cts.Token;

        var card = request.VaultId is { Length: > 0 }
            ? new CardRequest { VaultId = request.VaultId }
            : new CardRequest
            {
                Number = request.Card!.Number,
                Expiry = request.Card.Expiry,
                SecurityCode = request.Card.SecurityCode,
                Name = string.IsNullOrWhiteSpace(request.Card.CardholderName) ? null : request.Card.CardholderName,
                BillingAddress = BuildBillingAddress(request.Card),
            };

        var body = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits =
            [
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown { CurrencyCode = request.Currency, Value = Format(request.Amount) },
                    CustomId = request.OrderReference,
                    InvoiceId = request.InvoiceId,
                }
            ],
            PaymentSource = new PaymentSource { Card = card },
        };

        try
        {
            var order = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: request.RequestId,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: token);

            var payPalOrderId = order.Id ?? throw new PaymentGatewayException("PayPal did not return an order id.");

            if (order.Status == OrderStatus.PayerActionRequired)
                return Challenge(payPalOrderId);

            var auth = FirstAuthorization(order.PurchaseUnits);

            // Single-step card orders usually authorize on create; if not, run the explicit authorize step.
            if (auth is null)
            {
                var authorized = await _client.Orders.AuthorizeOrder(
                    id: payPalOrderId,
                    payPalMockResponse: null,
                    payPalRequestId: request.RequestId,
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: null,
                    prefer: "return=representation",
                    ct: token);

                if (authorized.Status == OrderStatus.PayerActionRequired)
                    return Challenge(payPalOrderId);

                auth = FirstAuthorization(authorized.PurchaseUnits);
            }

            if (auth?.Id is null)
                return new AuthorizationResult(payPalOrderId, null, AuthorizationOutcome.Failed, null, null, null,
                    "PayPal did not return an authorization for the order.");

            var outcome = auth.Status switch
            {
                var s when s == AuthorizationStatus.Created
                        || s == AuthorizationStatus.Captured
                        || s == AuthorizationStatus.PartiallyCaptured => AuthorizationOutcome.Authorized,
                var s when s == AuthorizationStatus.Pending => AuthorizationOutcome.Pending,
                _ => AuthorizationOutcome.Failed, // Denied, Voided, or unreadable/absent status
            };

            return new AuthorizationResult(
                payPalOrderId,
                auth.Id,
                outcome,
                auth.Status?.Value,
                ParseDate(auth.ExpirationTime),
                ParseDate(auth.CreateTime),
                outcome == AuthorizationOutcome.Failed ? $"authorization status {auth.Status?.Value ?? "unknown"}" : null);
        }
        catch (SdkException<CreateOrderError> ex) { throw Translate(ex.Error, TryTyped(ex.Error), "authorize", ex); }
        catch (SdkException<AuthorizeOrderError> ex) { throw Translate(ex.Error, TryTyped(ex.Error), "authorize", ex); }
        catch (Exception ex) when (IsTransport(ex, ct)) { throw Transport(ex, "authorize"); }
    }

    // ── Capture (fulfil) ─────────────────────────────────────────────────────
    public async Task<CaptureResult> CaptureAsync(string authorizationId, string requestId, CancellationToken ct)
    {
        using var cts = Deadline(ct);
        try
        {
            var cap = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: requestId,
                payPalAuthAssertion: null,
                body: new CaptureRequest { FinalCapture = true },
                prefer: "return=representation",
                ct: cts.Token);

            return MapCapture(cap);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex) { throw Translate(ex.Error, TryTyped(ex.Error), "fulfil", ex); }
        catch (Exception ex) when (IsTransport(ex, ct)) { throw Transport(ex, "fulfil"); }
    }

    public async Task<CaptureResult> GetCaptureAsync(string captureId, CancellationToken ct)
    {
        using var cts = Deadline(ct);
        try
        {
            var cap = await _client.Payments.GetCapturedPayment(captureId, payPalMockResponse: null, ct: cts.Token);
            return MapCapture(cap);
        }
        catch (SdkException<GetCapturedPaymentError> ex) { throw Translate(ex.Error, TryTyped(ex.Error), "read capture", ex); }
        catch (Exception ex) when (IsTransport(ex, ct)) { throw Transport(ex, "read capture"); }
    }

    // ── Reauthorize (renew a stale hold) ──────────────────────────────────────
    public async Task<AuthorizationInfo> ReauthorizeAsync(string authorizationId, string requestId, CancellationToken ct)
    {
        using var cts = Deadline(ct);
        try
        {
            var pa = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: requestId,
                payPalAuthAssertion: null,
                body: null,
                prefer: "return=representation",
                ct: cts.Token);
            return MapAuthorization(pa, authorizationId);
        }
        catch (SdkException<ReauthorizePaymentError> ex) { throw Translate(ex.Error, TryTyped(ex.Error), "reauthorize", ex); }
        catch (Exception ex) when (IsTransport(ex, ct)) { throw Transport(ex, "reauthorize"); }
    }

    public async Task<AuthorizationInfo> GetAuthorizationAsync(string authorizationId, CancellationToken ct)
    {
        using var cts = Deadline(ct);
        try
        {
            var pa = await _client.Payments.GetAuthorizedPayment(
                authorizationId, payPalMockResponse: null, payPalAuthAssertion: null, ct: cts.Token);
            return MapAuthorization(pa, authorizationId);
        }
        catch (SdkException<GetAuthorizedPaymentError> ex) { throw Translate(ex.Error, TryTyped(ex.Error), "read authorization", ex); }
        catch (Exception ex) when (IsTransport(ex, ct)) { throw Transport(ex, "read authorization"); }
    }

    // ── Void (cancel) ──────────────────────────────────────────────────────────
    public async Task VoidAsync(string authorizationId, CancellationToken ct)
    {
        using var cts = Deadline(ct);
        try
        {
            await _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: null,
                prefer: "return=representation",
                ct: cts.Token);
        }
        catch (SdkException<VoidPaymentError> ex) { throw Translate(ex.Error, TryTyped(ex.Error), "cancel", ex); }
        // A successful void answers 204 No Content; the SDK then throws JsonException trying to deserialize an
        // empty body into PaymentAuthorization. That is success, not a failure — swallow it.
        catch (JsonException) { }
        catch (Exception ex) when (IsTransport(ex, ct)) { throw Transport(ex, "cancel"); }
    }

    // ── Refund ─────────────────────────────────────────────────────────────────
    public async Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, CancellationToken ct)
    {
        using var cts = Deadline(ct);
        // Omit amount for a full refund (provider default); set it for a partial refund.
        var body = amount.HasValue
            ? new RefundRequest { Amount = new Money { CurrencyCode = currency, Value = Format(amount.Value) } }
            : new RefundRequest();
        try
        {
            var refund = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: cts.Token);

            var refundId = refund.Id ?? throw new PaymentGatewayException("PayPal did not return a refund id.");
            var outcome = refund.Status switch
            {
                var s when s == RefundStatus.Completed => RefundOutcome.Completed,
                var s when s == RefundStatus.Pending => RefundOutcome.Pending,
                _ => RefundOutcome.Failed, // Failed, Cancelled, or unreadable/absent
            };
            return new RefundResult(refundId, outcome, refund.Status?.Value, ParseMoney(refund.Amount), currency);
        }
        catch (SdkException<RefundCapturedPaymentError> ex) { throw Translate(ex.Error, TryTyped(ex.Error), "refund", ex); }
        catch (Exception ex) when (IsTransport(ex, ct)) { throw Transport(ex, "refund"); }
    }

    // ── Vault a card ────────────────────────────────────────────────────────────
    public async Task<VaultCardResult> VaultCardAsync(CardDetails card, string merchantCustomerId,
        string? existingCustomerId, string requestId, CancellationToken ct)
    {
        using var cts = Deadline(ct);
        var token = cts.Token;
        var customer = new Customer
        {
            MerchantCustomerId = SanitizeCustomerId(merchantCustomerId),
            Id = string.IsNullOrWhiteSpace(existingCustomerId) ? null : existingCustomerId,
        };
        var cardholder = string.IsNullOrWhiteSpace(card.CardholderName) ? null : card.CardholderName;

        try
        {
            // Server-side card vaulting is a two-step flow (no browser): create a setup token holding the raw
            // card, then exchange it for a permanent payment token. (Creating a payment token directly from a
            // raw card is not supported for a new vault entry and returns 500.)
            var setup = await _client.Vault.CreateSetupToken(
                payPalRequestId: requestId,
                body: new SetupTokenRequest
                {
                    Customer = customer,
                    PaymentSource = new SetupTokenRequestPaymentSource
                    {
                        Card = new SetupTokenRequestCard
                        {
                            Number = card.Number,
                            Expiry = card.Expiry,
                            SecurityCode = card.SecurityCode,
                            Name = cardholder,
                            BillingAddress = BuildBillingAddress(card),
                        },
                    },
                },
                ct: token);

            var setupId = setup.Id ?? throw new PaymentGatewayException("PayPal did not return a setup token id.");

            var resp = await _client.Vault.CreatePaymentToken(
                payPalRequestId: requestId + "-pt",
                body: new PaymentTokenRequest
                {
                    Customer = customer,
                    PaymentSource = new PaymentTokenRequestPaymentSource
                    {
                        Token = new VaultTokenRequest { Id = setupId, Type = VaultTokenRequestType.SetupToken },
                    },
                },
                ct: token);

            var tokenId = resp.Id ?? throw new PaymentGatewayException("PayPal did not return a vault token id.");
            var c = resp.PaymentSource?.Card;
            return new VaultCardResult(tokenId, resp.Customer?.Id ?? setup.Customer?.Id,
                c?.Brand?.Value, c?.LastDigits, c?.Expiry, c?.Name);
        }
        catch (SdkException<CreateSetupTokenError> ex) { throw Translate(ex.Error, TryTyped(ex.Error), "save card", ex); }
        catch (SdkException<CreatePaymentTokenError> ex) { throw Translate(ex.Error, TryTyped(ex.Error), "save card", ex); }
        catch (Exception ex) when (IsTransport(ex, ct)) { throw Transport(ex, "save card"); }
    }

    public async Task DeleteVaultTokenAsync(string tokenId, CancellationToken ct)
    {
        using var cts = Deadline(ct);
        try
        {
            await _client.Vault.DeletePaymentToken(tokenId, ct: cts.Token);
        }
        catch (SdkException<DeletePaymentTokenError> ex) { throw Translate(ex.Error, TryTyped(ex.Error), "remove card", ex); }
        catch (Exception ex) when (IsTransport(ex, ct)) { throw Transport(ex, "remove card"); }
    }

    // ── Reconciliation (Case B error) ────────────────────────────────────────────
    public async Task<ReconciliationFetch> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct)
    {
        using var cts = Deadline(ct);
        var token = cts.Token;
        var start = FormatDate(from);
        var end = FormatDate(to);
        var results = new List<PayPalTransaction>();
        int page = 1, totalPages = 1, pagesFetched = 0;
        var truncated = false;

        try
        {
            do
            {
                var resp = await _client.TransactionSearch.SearchTransactions(
                    startDate: start,
                    endDate: end,
                    transactionId: null,
                    transactionType: null,
                    transactionStatus: null,
                    transactionAmount: null,
                    transactionCurrency: null,
                    paymentInstrumentType: null,
                    storeId: null,
                    terminalId: null,
                    fields: "transaction_info",
                    balanceAffectingRecordsOnly: "Y",
                    pageSize: ReconciliationPageSize,
                    page: page,
                    ct: token);

                totalPages = resp.TotalPages ?? 1;
                foreach (var detail in resp.TransactionDetails ?? Enumerable.Empty<TransactionDetails>())
                {
                    var info = detail.TransactionInfo;
                    results.Add(new PayPalTransaction(
                        info?.TransactionId,
                        info?.InvoiceId,
                        ParseMoney(info?.TransactionAmount),
                        info?.TransactionAmount?.CurrencyCode,
                        info?.TransactionStatus,
                        ParseDate(info?.TransactionInitiationDate)));
                }

                pagesFetched++;
                if (page >= MaxReconciliationPages && page < totalPages)
                {
                    truncated = true;
                    _logger.LogWarning("Reconciliation page cap reached ({Cap}); {Total} pages available — result truncated.",
                        MaxReconciliationPages, totalPages);
                    break;
                }
                page++;
            }
            while (page <= totalPages);

            return new ReconciliationFetch(results, pagesFetched, totalPages, truncated);
        }
        catch (SdkException<RawError> ex) // Case B
        {
            throw new PaymentGatewayException(
                $"PayPal transaction search failed (HTTP {(int)ex.Error.StatusCode}).", inner: ex);
        }
        catch (Exception ex) when (IsTransport(ex, ct)) { throw Transport(ex, "reconciliation"); }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────
    private CancellationTokenSource Deadline(CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_callBudget);
        return cts;
    }

    private static AuthorizationResult Challenge(string payPalOrderId) => new(
        payPalOrderId, null, AuthorizationOutcome.ChallengeRequired, null, null, null,
        "PayPal requires shopper approval (3DS/PAYER_ACTION_REQUIRED) for this card.");

    private static AuthorizationWithAdditionalData? FirstAuthorization(IReadOnlyList<PurchaseUnit>? purchaseUnits) =>
        purchaseUnits?
            .SelectMany(pu => pu.Payments?.Authorizations ?? Enumerable.Empty<AuthorizationWithAdditionalData>())
            .FirstOrDefault();

    private CaptureResult MapCapture(CapturedPayment cap)
    {
        var captureId = cap.Id ?? throw new PaymentGatewayException("PayPal did not return a capture id.");
        var outcome = cap.Status switch
        {
            var s when s == CaptureStatus.Completed
                    || s == CaptureStatus.Refunded
                    || s == CaptureStatus.PartiallyRefunded => CaptureOutcome.Completed,
            var s when s == CaptureStatus.Pending => CaptureOutcome.Pending,
            _ => CaptureOutcome.Failed, // Declined, Failed, or unreadable/absent
        };
        var breakdown = cap.SellerReceivableBreakdown;
        return new CaptureResult(
            captureId,
            outcome,
            cap.Status?.Value,
            ParseMoney(breakdown?.GrossAmount) ?? ParseMoney(cap.Amount),
            ParseMoney(breakdown?.PaypalFee),
            ParseMoney(breakdown?.NetAmount),
            cap.Amount?.CurrencyCode ?? _currency,
            ParseDate(cap.CreateTime));
    }

    private static AuthorizationInfo MapAuthorization(PaymentAuthorization pa, string fallbackId)
    {
        var captured = pa.Status == AuthorizationStatus.Captured || pa.Status == AuthorizationStatus.PartiallyCaptured;
        var usable = pa.Status == AuthorizationStatus.Created;
        return new AuthorizationInfo(pa.Id ?? fallbackId, pa.Status?.Value, ParseDate(pa.ExpirationTime), captured, usable);
    }

    private static Address? BuildBillingAddress(CardDetails card)
    {
        // Address.CountryCode is required by the SDK, so only send a billing address when a country is supplied.
        if (string.IsNullOrWhiteSpace(card.BillingCountryCode))
            return null;
        return new Address
        {
            CountryCode = card.BillingCountryCode!,
            AddressLine1 = card.BillingLine1,
            AdminArea2 = card.BillingCity,
            AdminArea1 = card.BillingState,
            PostalCode = card.BillingPostalCode,
        };
    }

    private static string SanitizeCustomerId(string buyerId)
    {
        // PayPal's vault backend 500s on some characters the schema regex nominally allows (e.g. '@' in an
        // email). Reduce to a conservative [0-9A-Za-z_-] id, capped at 64 chars, so grouping is stable and safe.
        var cleaned = new string(buyerId.Select(ch =>
            char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-').ToArray());
        if (cleaned.Length == 0) cleaned = "customer";
        return cleaned.Length <= 64 ? cleaned : cleaned.Substring(0, 64);
    }

    private static Error? TryTyped<TError>(TError error) where TError : ApiError
    {
        // Every Case-A {Operation}Error exposes TryGetError(out Error); resolve it via the concrete instance.
        return error switch
        {
            CreateOrderError e => e.TryGetError(out var x) ? x : null,
            AuthorizeOrderError e => e.TryGetError(out var x) ? x : null,
            CaptureAuthorizedPaymentError e => e.TryGetError(out var x) ? x : null,
            GetCapturedPaymentError e => e.TryGetError(out var x) ? x : null,
            ReauthorizePaymentError e => e.TryGetError(out var x) ? x : null,
            GetAuthorizedPaymentError e => e.TryGetError(out var x) ? x : null,
            VoidPaymentError e => e.TryGetError(out var x) ? x : null,
            RefundCapturedPaymentError e => e.TryGetError(out var x) ? x : null,
            CreatePaymentTokenError e => e.TryGetError(out var x) ? x : null,
            CreateSetupTokenError e => e.TryGetError(out var x) ? x : null,
            DeletePaymentTokenError e => e.TryGetError(out var x) ? x : null,
            _ => null,
        };
    }

    /// <summary>
    /// Translates a typed SDK error into a caller-safe PaymentException. Our-credential/rate-limit codes
    /// (and unreadable errors) become a gateway (5xx) failure; everything else the provider rejected is a
    /// caller-actionable validation failure (e.g. a declined card). Logs the PayPal debug_id for support.
    /// </summary>
    private PaymentException Translate<TError>(TError apiError, Error? typed, string op, Exception original)
        where TError : ApiError
    {
        if (typed is not null)
        {
            var issues = typed.Details is null ? "" :
                string.Join("; ", typed.Details.Select(d => $"{d.Issue}:{d.Field}:{d.Description}"));
            _logger.LogWarning("PayPal {Op} rejected: {Name} — {Message} (debug_id={DebugId}) details=[{Issues}]",
                op, typed.Name, typed.Message, typed.DebugId, issues);
            if (IsOurFault(typed.Name))
                return new PaymentGatewayException($"Payment provider unavailable during {op}.", typed.DebugId, original);
            return new PaymentValidationException($"PayPal rejected the {op}: {typed.Name} — {typed.Message}");
        }

        if (apiError.TryGetRawError(out var raw))
        {
            var status = (int)raw.StatusCode;
            _logger.LogWarning("PayPal {Op} failed with HTTP {Status}.", op, status);
            if (status is 401 or 403 or 429 or >= 500)
                return new PaymentGatewayException($"Payment provider unavailable during {op} (HTTP {status}).", inner: original);
            return new PaymentValidationException($"PayPal rejected the {op} (HTTP {status}).");
        }

        _logger.LogWarning("PayPal {Op} failed with an unrecognised error shape.", op);
        return new PaymentGatewayException($"Payment provider error during {op}.", inner: original);
    }

    private static bool IsOurFault(string name) =>
        name.Contains("AUTHENTICATION", StringComparison.OrdinalIgnoreCase)
        || name.Contains("AUTHORIZATION_ERROR", StringComparison.OrdinalIgnoreCase)
        || name.Equals("NOT_AUTHORIZED", StringComparison.OrdinalIgnoreCase)
        || name.Equals("PERMISSION_DENIED", StringComparison.OrdinalIgnoreCase)
        || name.Equals("RATE_LIMIT_REACHED", StringComparison.OrdinalIgnoreCase)
        || name.Equals("INTERNAL_SERVER_ERROR", StringComparison.OrdinalIgnoreCase);

    private static bool IsTransport(Exception ex, CancellationToken callerToken) =>
        ex is HttpRequestException
        || ex is JsonException                                 // a 2xx body that no longer matches the model
        // The SDK's per-attempt timeout and our own call-budget both surface as OperationCanceledException;
        // treat them as transport failures — but let a genuine caller cancellation propagate untouched.
        || (ex is OperationCanceledException && !callerToken.IsCancellationRequested);

    private PaymentGatewayException Transport(Exception ex, string op)
    {
        _logger.LogWarning(ex, "PayPal {Op} could not be completed (transport/timeout/parse failure).", op);
        return ex switch
        {
            JsonException => new PaymentGatewayException($"PayPal returned an unreadable response during {op}.", inner: ex),
            OperationCanceledException => new PaymentGatewayException($"Payment provider timed out during {op}.", inner: ex),
            _ => new PaymentGatewayException($"Payment provider was unreachable during {op}.", inner: ex),
        };
    }

    private static string Format(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d) ? d : null;

    private static decimal? ParseMoney(Money? money) =>
        money is not null && decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var v)
            ? v : null;
}
