using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using SdkError = PayPalServerSdk.Models.Error;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// The PayPal implementation of <see cref="IPaymentProcessor"/> and the only place this application talks
/// to PayPal. Provider failures are translated into <see cref="PaymentGatewayException"/>; card details are
/// never logged. Real idempotency keys are carried on the PayPal-Request-Id of each write; the SDK never
/// resends a POST, so a double-click cannot double-charge.
/// </summary>
public class PayPalPaymentProcessor : IPaymentProcessor
{
    private readonly PayPalServerSdkClient _client;
    private readonly IAppLogger<PayPalPaymentProcessor> _logger;

    private const int CallBudgetSeconds = 90;
    private const int ReportBudgetSeconds = 180;

    public PayPalPaymentProcessor(PayPalServerSdkClient client, IAppLogger<PayPalPaymentProcessor> logger)
    {
        _client = client;
        _logger = logger;
    }

    // ---------- Authorize (create order + authorize) ----------

    public async Task<AuthorizationResult> AuthorizeAsync(AuthorizationRequest request, CancellationToken cancellationToken)
    {
        using var cts = Budget(cancellationToken, CallBudgetSeconds);
        var ct = cts.Token;

        try
        {
            // Single-step direct card: supply the card (or vault token) in the CREATE order request with
            // intent AUTHORIZE. PayPal processes the card and returns the authorization (the hold) in the
            // same response — a valid payment_source is required, which is exactly what we provide here.
            var (card, oneOffLastDigits) = BuildCard(request.Instrument);
            var orderBody = new OrderRequest
            {
                Intent = CheckoutPaymentIntent.Authorize,
                PurchaseUnits = new List<PurchaseUnitRequest>
                {
                    new()
                    {
                        ReferenceId = "default",
                        InvoiceId = request.PaymentReference,        // globally unique (account requires it)
                        CustomId = request.ReconciliationReference,  // stable eShop ref for reconciliation
                        Amount = new AmountWithBreakdown
                        {
                            CurrencyCode = request.CurrencyCode,
                            Value = Format(request.Amount)
                        }
                    }
                },
                PaymentSource = new PaymentSource { Card = card }
            };

            Order created;
            try
            {
                created = await _client.Orders.CreateOrder(
                    payPalMockResponse: null,
                    payPalRequestId: request.PaymentReference, // mandatory & unique for single-step card orders
                    payPalPartnerAttributionId: null,
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: orderBody,
                    ct: ct);
            }
            catch (SdkException<CreateOrderError> ex)
            {
                ex.Error.TryGetError(out var typed);
                ex.Error.TryGetRawError(out var raw);
                throw Translate(typed, raw, ex);
            }

            var processorOrderId = created.Id
                ?? throw new PaymentGatewayException("PayPal did not return an order id.",
                    PaymentGatewayFailureKind.Unreadable);

            return InterpretAuthorization(request, processorOrderId, created, oneOffLastDigits);
        }
        catch (JsonException ex) { throw Parse(ex); }
        catch (Exception ex) when (IsTransport(ex, ct, cancellationToken)) { throw Transport(ex); }
    }

    private AuthorizationResult InterpretAuthorization(AuthorizationRequest request, string processorOrderId,
        Order order, string? oneOffLastDigits)
    {
        // A challenge (3DS / payer approval) is not supported here — stop and report it.
        if (order.Status == OrderStatus.PayerActionRequired ||
            (order.Links?.Any(l => l.Rel.Contains("payer-action", StringComparison.OrdinalIgnoreCase)
                                || l.Rel.Contains("3ds", StringComparison.OrdinalIgnoreCase)) ?? false))
        {
            return new AuthorizationResult(AuthorizationOutcome.ChallengeRequired, processorOrderId, null,
                order.Status?.Value ?? "PAYER_ACTION_REQUIRED", request.Amount, request.CurrencyCode,
                null, null, "Browser-based verification required.");
        }

        var authorization = order.PurchaseUnits?
            .FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();

        var respCard = order.PaymentSource?.Card;
        var description = respCard is not null
            ? $"{respCard.Brand?.Value ?? "CARD"} ending {respCard.LastDigits}"
            : oneOffLastDigits is not null ? $"CARD ending {oneOffLastDigits}" : null;

        if (authorization?.Id is null)
        {
            // No hold was created and it is not a challenge — treat as a decline the shopper can act on.
            return new AuthorizationResult(AuthorizationOutcome.Declined, processorOrderId, null,
                authorization?.Status?.Value ?? order.Status?.Value ?? "UNKNOWN",
                request.Amount, request.CurrencyCode, null, description,
                "The card could not be authorized.");
        }

        var rawStatus = authorization.Status?.Value ?? "CREATED";
        if (authorization.Status == AuthorizationStatus.Denied)
        {
            return new AuthorizationResult(AuthorizationOutcome.Declined, processorOrderId, authorization.Id,
                rawStatus, request.Amount, request.CurrencyCode, null, description, "The card was declined.");
        }

        var outcome = authorization.Status == AuthorizationStatus.Pending
            ? AuthorizationOutcome.Pending
            : AuthorizationOutcome.Authorized;

        return new AuthorizationResult(outcome, processorOrderId, authorization.Id, rawStatus,
            request.Amount, request.CurrencyCode, ParseDate(authorization.ExpirationTime), description, null);
    }

    // ---------- Capture ----------

    public async Task<CaptureResult> CaptureAsync(string paymentReference, string authorizationId, decimal amount,
        string currencyCode, CancellationToken cancellationToken)
    {
        using var cts = Budget(cancellationToken, CallBudgetSeconds);
        var ct = cts.Token;
        try
        {
            // No invoice_id on the capture: the account enforces invoice-id uniqueness and the capture
            // inherits the order's custom_id for reconciliation anyway.
            var body = new CaptureRequest
            {
                Amount = new Money { CurrencyCode = currencyCode, Value = Format(amount) },
                FinalCapture = true
            };

            CapturedPayment capture;
            try
            {
                capture = await _client.Payments.CaptureAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalRequestId: $"capture-{paymentReference}",
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: "return=representation", // ask for the full body so the fee breakdown is present
                    ct: ct);
            }
            catch (SdkException<CaptureAuthorizedPaymentError> ex)
            {
                throw TranslatePayments(ex.Error, ex);
            }

            var status = capture.Status?.Value ?? "UNKNOWN";
            if (capture.Status != CaptureStatus.Completed && capture.Status != CaptureStatus.Pending)
            {
                // Not captured — surface as a rejection so fulfilment can attempt a renewal/retry, then fail.
                throw new PaymentGatewayException($"PayPal did not capture the payment (status {status}).",
                    PaymentGatewayFailureKind.RequestRejected);
            }

            var breakdown = capture.SellerReceivableBreakdown;
            var gross = breakdown?.GrossAmount is { } g ? ParseMoney(g) : amount;
            var fee = breakdown?.PaypalFee is { } f ? ParseMoney(f) : (decimal?)null;
            var net = breakdown?.NetAmount is { } n ? ParseMoney(n) : (decimal?)null;

            return new CaptureResult(capture.Id ?? "", status, gross, fee, net, currencyCode,
                capture.Status == CaptureStatus.Completed);
        }
        catch (JsonException ex) { throw Parse(ex); }
        catch (Exception ex) when (IsTransport(ex, ct, cancellationToken)) { throw Transport(ex); }
    }

    // ---------- Reauthorize ----------

    public async Task<ReauthorizeResult> ReauthorizeAsync(string paymentReference, string authorizationId,
        decimal amount, string currencyCode, CancellationToken cancellationToken)
    {
        using var cts = Budget(cancellationToken, CallBudgetSeconds);
        var ct = cts.Token;
        try
        {
            var body = new ReauthorizeRequest
            {
                Amount = new Money { CurrencyCode = currencyCode, Value = Format(amount) }
            };

            PaymentAuthorization reauth;
            try
            {
                reauth = await _client.Payments.ReauthorizePayment(
                    authorizationId: authorizationId,
                    payPalRequestId: $"reauth-{paymentReference}",
                    payPalAuthAssertion: null,
                    body: body,
                    ct: ct);
            }
            catch (SdkException<ReauthorizePaymentError> ex)
            {
                var translated = TranslatePayments(ex.Error, ex);
                // A rejection means the hold can no longer be renewed (e.g. past 30 days) — report, don't throw.
                if (translated.Kind == PaymentGatewayFailureKind.RequestRejected)
                    return new ReauthorizeResult(false, null, "FAILED", null, translated.Message);
                throw translated;
            }

            if (reauth.Id is null || reauth.Status == AuthorizationStatus.Denied)
                return new ReauthorizeResult(false, null, reauth.Status?.Value ?? "DENIED", null,
                    "PayPal declined to renew the authorization.");

            return new ReauthorizeResult(true, reauth.Id, reauth.Status?.Value ?? "CREATED",
                ParseDate(reauth.ExpirationTime), null);
        }
        catch (JsonException ex) { throw Parse(ex); }
        catch (Exception ex) when (IsTransport(ex, ct, cancellationToken)) { throw Transport(ex); }
    }

    // ---------- Void ----------

    public async Task VoidAsync(string paymentReference, string authorizationId, CancellationToken cancellationToken)
    {
        using var cts = Budget(cancellationToken, CallBudgetSeconds);
        var ct = cts.Token;
        try
        {
            try
            {
                await _client.Payments.VoidPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    payPalRequestId: $"void-{paymentReference}",
                    ct: ct);
            }
            catch (SdkException<VoidPaymentError> ex)
            {
                throw TranslatePayments(ex.Error, ex);
            }
            // A successful void returns 204 No Content, which the SDK cannot deserialize into
            // PaymentAuthorization — the resulting JsonException means the void succeeded.
            catch (JsonException) { }
        }
        catch (Exception ex) when (IsTransport(ex, ct, cancellationToken)) { throw Transport(ex); }
    }

    // ---------- Refund ----------

    public async Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currencyCode,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        using var cts = Budget(cancellationToken, CallBudgetSeconds);
        var ct = cts.Token;
        try
        {
            // Full refund => empty body; partial => an amount. The caller idempotency key is the real key.
            var body = amount is decimal amt
                ? new RefundRequest { Amount = new Money { CurrencyCode = currencyCode, Value = Format(amt) } }
                : new RefundRequest();

            Refund refund;
            try
            {
                refund = await _client.Payments.RefundCapturedPayment(
                    captureId: captureId,
                    payPalMockResponse: null,
                    // Namespace the caller key by capture so the same key against different captures never
                    // collides at PayPal, while a repeat under the same key on the same capture stays idempotent.
                    payPalRequestId: $"refund-{captureId}-{idempotencyKey}",
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: "return=representation",
                    ct: ct);
            }
            catch (SdkException<RefundCapturedPaymentError> ex)
            {
                throw TranslatePayments(ex.Error, ex);
            }

            var refunded = refund.Amount is { } m ? ParseMoney(m) : amount ?? 0m;
            return new RefundResult(refund.Id ?? "", refund.Status?.Value ?? "UNKNOWN", refunded);
        }
        catch (JsonException ex) { throw Parse(ex); }
        catch (Exception ex) when (IsTransport(ex, ct, cancellationToken)) { throw Transport(ex); }
    }

    // ---------- Vault a card ----------

    public async Task<SavedCardResult> VaultCardAsync(VaultCardRequest request, CancellationToken cancellationToken)
    {
        using var cts = Budget(cancellationToken, CallBudgetSeconds);
        var ct = cts.Token;
        try
        {
            var body = new PaymentTokenRequest
            {
                Customer = request.PayPalCustomerId is not null
                    ? new Customer { Id = request.PayPalCustomerId }
                    : null,
                PaymentSource = new PaymentTokenRequestPaymentSource
                {
                    Card = new PaymentTokenRequestCard
                    {
                        Number = request.Card.Number,
                        Expiry = request.Card.Expiry,
                        SecurityCode = request.Card.SecurityCode,
                        Name = request.Card.CardholderName,
                        BillingAddress = MapAddress(request.BillingAddress)
                    }
                }
            };

            PaymentTokenResponse resp;
            try
            {
                resp = await _client.Vault.CreatePaymentToken(payPalRequestId: null, body: body, ct: ct);
            }
            catch (SdkException<CreatePaymentTokenError> ex)
            {
                ex.Error.TryGetError(out var typed);
                ex.Error.TryGetRawError(out var raw);
                throw Translate(typed, raw, ex);
            }

            var vaultToken = resp.Id
                ?? throw new PaymentGatewayException("PayPal did not return a vault token.",
                    PaymentGatewayFailureKind.Unreadable);

            var respCard = resp.PaymentSource?.Card;
            var lastDigits = respCard?.LastDigits ?? Last4(request.Card.Number);
            var brand = respCard?.Brand?.Value ?? "CARD";
            var expiry = respCard?.Expiry ?? request.Card.Expiry;
            var name = respCard?.Name ?? request.Card.CardholderName;

            return new SavedCardResult(vaultToken, resp.Customer?.Id, brand, lastDigits, expiry, name);
        }
        catch (JsonException ex) { throw Parse(ex); }
        catch (Exception ex) when (IsTransport(ex, ct, cancellationToken)) { throw Transport(ex); }
    }

    // ---------- Delete a vaulted card ----------

    public async Task DeleteVaultedCardAsync(string vaultToken, CancellationToken cancellationToken)
    {
        using var cts = Budget(cancellationToken, CallBudgetSeconds);
        var ct = cts.Token;
        try
        {
            try
            {
                await _client.Vault.DeletePaymentToken(id: vaultToken, ct: ct);
            }
            catch (SdkException<DeletePaymentTokenError> ex)
            {
                ex.Error.TryGetError(out var typed);
                ex.Error.TryGetRawError(out var raw);
                var translated = Translate(typed, raw, ex);
                // Already gone at PayPal is a success for our purposes.
                if (translated.ProviderStatus == HttpStatusCode.NotFound) return;
                throw translated;
            }
        }
        catch (JsonException ex) { throw Parse(ex); }
        catch (Exception ex) when (IsTransport(ex, ct, cancellationToken)) { throw Transport(ex); }
    }

    // ---------- Reconciliation (paged, whole range) ----------

    public async Task<IReadOnlyList<ReconciliationTransaction>> ListTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken)
    {
        using var cts = Budget(cancellationToken, ReportBudgetSeconds);
        var ct = cts.Token;

        var results = new List<ReconciliationTransaction>();

        const int pageSize = 500;
        const int maxPages = 1000;                        // per-window backstop
        var window = TimeSpan.FromDays(30);               // PayPal caps a single query at 31 days

        try
        {
            // Walk the whole range in <=30-day windows so an arbitrarily long range is fully covered.
            for (var windowStart = from; windowStart < to; windowStart += window)
            {
                var windowEnd = windowStart + window;
                if (windowEnd > to) windowEnd = to;

                var startDate = FormatDate(windowStart);
                var endDate = FormatDate(windowEnd);
                int page = 1;

                while (true)
                {
                    SearchResponse resp;
                    try
                    {
                        resp = await _client.TransactionSearch.SearchTransactions(
                            startDate: startDate,
                            endDate: endDate,
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
                            pageSize: pageSize,
                            page: page,
                            ct: ct);
                    }
                    catch (SdkException<RawError> ex)
                    {
                        // SearchTransactions is Case B — the error IS a RawError.
                        _logger.LogWarning($"SearchTransactions failed {(int)ex.Error.StatusCode}: {ex.Error.ReadAsString()}");
                        throw Translate(null, ex.Error, ex);
                    }

                    foreach (var detail in resp.TransactionDetails ?? Enumerable.Empty<TransactionDetails>())
                    {
                        var info = detail.TransactionInfo;
                        if (info is null) continue;
                        results.Add(new ReconciliationTransaction(
                            info.TransactionId ?? "",
                            info.TransactionStatus,
                            info.TransactionAmount is { } amt ? ParseMoney(amt) : (decimal?)null,
                            info.TransactionAmount?.CurrencyCode,
                            info.InvoiceId,
                            info.CustomField,
                            ParseDate(info.TransactionInitiationDate)));
                    }

                    var totalPages = resp.TotalPages ?? 1;
                    if (page >= totalPages || page >= maxPages) break;
                    page++;
                }
            }
        }
        catch (JsonException ex) { throw Parse(ex); }
        catch (Exception ex) when (IsTransport(ex, ct, cancellationToken)) { throw Transport(ex); }

        return results;
    }

    // ---------- helpers ----------

    private static CancellationTokenSource Budget(CancellationToken ct, int seconds)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(seconds));
        return cts;
    }

    private (CardRequest card, string? oneOffLastDigits) BuildCard(PaymentInstrument instrument)
    {
        if (!string.IsNullOrEmpty(instrument.VaultToken))
            return (new CardRequest { VaultId = instrument.VaultToken }, null);

        var card = instrument.Card!;
        return (new CardRequest
        {
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            Name = card.CardholderName,
            BillingAddress = MapAddress(instrument.BillingAddress)
        }, Last4(card.Number));
    }

    private static Address? MapAddress(BillingAddress? address)
    {
        if (address is null) return null;
        return new Address
        {
            AddressLine1 = address.AddressLine1,
            AdminArea2 = address.AdminArea2,
            AdminArea1 = address.AdminArea1,
            PostalCode = address.PostalCode,
            CountryCode = address.CountryCode
        };
    }

    private static string Format(decimal amount) =>
        decimal.Round(amount, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal ParseMoney(Money money) =>
        decimal.Parse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture);

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var parsed) ? parsed : null;

    private static string Last4(string number) =>
        number.Length >= 4 ? number[^4..] : number;

    private static bool IsTransport(Exception ex, CancellationToken ct, CancellationToken callerCt) =>
        (ex is HttpRequestException || ex is TaskCanceledException || ex is OperationCanceledException)
        && !callerCt.IsCancellationRequested;

    private PaymentGatewayException Transport(Exception ex)
    {
        _logger.LogWarning($"PayPal call failed at the transport layer: {ex.GetType().Name}. Outcome may be unknown.");
        return new PaymentGatewayException(
            "The payment provider could not be reached in time. The outcome may be unknown; please reconcile.",
            PaymentGatewayFailureKind.ProviderUnavailable, null, null, ex);
    }

    private PaymentGatewayException Parse(JsonException ex)
    {
        _logger.LogWarning("PayPal returned a response that could not be processed.");
        return new PaymentGatewayException(
            "The payment provider returned a response that could not be processed.",
            PaymentGatewayFailureKind.Unreadable, null, null, ex);
    }

    /// <summary>Translates a Payments-controller typed error (TryGetError + TryGetNoContent + fallback).</summary>
    private PaymentGatewayException TranslatePayments(ApiError apiError, Exception source)
    {
        // Concrete Payments error types share TryGetError(out Error); resolve via the runtime instance.
        SdkError? typed = null;
        RawError? raw = null;
        switch (apiError)
        {
            case CaptureAuthorizedPaymentError e:
                e.TryGetError(out typed); if (typed is null && e.TryGetNoContent(out var c1)) raw = c1; break;
            case ReauthorizePaymentError e:
                e.TryGetError(out typed); if (typed is null && e.TryGetNoContent(out var c2)) raw = c2; break;
            case VoidPaymentError e:
                e.TryGetError(out typed); if (typed is null && e.TryGetNoContent(out var c3)) raw = c3; break;
            case RefundCapturedPaymentError e:
                e.TryGetError(out typed); if (typed is null && e.TryGetNoContent(out var c4)) raw = c4; break;
        }
        if (typed is null && raw is null) apiError.TryGetRawError(out raw);
        return Translate(typed, raw, source);
    }

    private PaymentGatewayException Translate(SdkError? typed, RawError? raw, Exception source)
    {
        var status = raw?.StatusCode;
        if (typed is not null)
        {
            var kind = ClassifyName(typed.Name, status);
            var issues = typed.Details is null ? "" :
                string.Join("; ", typed.Details.Select(d => $"{d.Issue}{(d.Field is null ? "" : $"@{d.Field}")}: {d.Description}"));
            _logger.LogWarning($"PayPal error {typed.Name} (debug_id {typed.DebugId}) [{issues}].");
            var message = kind == PaymentGatewayFailureKind.ProviderUnavailable
                ? "The payment provider is temporarily unavailable. Please try again later."
                : $"PayPal rejected the request ({typed.Name}).";
            return new PaymentGatewayException(message, kind, status, typed.DebugId, source);
        }
        if (status is HttpStatusCode s)
        {
            var kind = ClassifyStatus(s);
            _logger.LogWarning($"PayPal returned HTTP {(int)s}.");
            var message = kind == PaymentGatewayFailureKind.ProviderUnavailable
                ? "The payment provider is temporarily unavailable. Please try again later."
                : $"PayPal rejected the request (HTTP {(int)s}).";
            return new PaymentGatewayException(message, kind, s, null, source);
        }
        return new PaymentGatewayException("PayPal returned an unrecognised error.",
            PaymentGatewayFailureKind.Unreadable, null, null, source);
    }

    private static PaymentGatewayFailureKind ClassifyName(string name, HttpStatusCode? status)
    {
        var upper = name.ToUpperInvariant();
        if (upper is "NOT_AUTHORIZED" or "PERMISSION_DENIED" or "AUTHENTICATION_FAILURE"
            or "RATE_LIMIT_REACHED" or "INTERNAL_SERVER_ERROR")
            return PaymentGatewayFailureKind.ProviderUnavailable;
        if (status is HttpStatusCode s && (int)s >= 500)
            return PaymentGatewayFailureKind.ProviderUnavailable;
        return PaymentGatewayFailureKind.RequestRejected;
    }

    private static PaymentGatewayFailureKind ClassifyStatus(HttpStatusCode status) =>
        (int)status switch
        {
            401 or 403 or 429 => PaymentGatewayFailureKind.ProviderUnavailable,
            >= 500 => PaymentGatewayFailureKind.ProviderUnavailable,
            _ => PaymentGatewayFailureKind.RequestRejected
        };
}
