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
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// The single seam between the app and the PayPal SDK. Builds SDK request models from domain inputs,
/// reads the fields the flows need out of SDK responses, and translates every PayPal failure to
/// <see cref="PayPalGatewayException"/> so SDK types never leak upward. Never logs card data or request bodies.
/// </summary>
public class PayPalPaymentGateway : IPayPalPaymentGateway
{
    // Whole-call budget: the SDK's per-attempt Timeout does not bound a whole (retryable) call — only a
    // CancellationToken deadline does. Every call runs under this linked deadline.
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(60);

    // PayPal's Transaction Search accepts at most 31 days per request; a wider range is chunked.
    private static readonly TimeSpan MaxSearchWindow = TimeSpan.FromDays(31);
    private const int SearchPageSize = 100;
    private const int MaxPagesPerWindow = 200; // safety backstop against an uncooperative pager
    private const int MaxWindows = 120;        // ~10 years of 31-day windows

    private readonly PayPalServerSdkClient _client;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalPaymentGateway> _logger;

    public PayPalPaymentGateway(PayPalServerSdkClient client, IOptions<PayPalOptions> options,
        ILogger<PayPalPaymentGateway> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    private string Currency => _options.Currency;

    public async Task<AuthorizationResult> AuthorizeAsync(AuthorizePaymentRequest request, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        var deadline = cts.Token;

        var card = BuildCardRequest(request.Card, request.VaultId);
        var orderRequest = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new[]
            {
                new PurchaseUnitRequest
                {
                    ReferenceId = "default",
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = request.Currency,
                        Value = CurrencyFormatter.Format(request.Amount, request.Currency)
                    },
                    InvoiceId = request.InvoiceId,
                    CustomId = request.CustomId,
                    Description = request.Description
                }
            },
            PaymentSource = new PaymentSource { Card = card }
        };

        // CreateOrder with a card is a "single-step" create — PayPal-Request-Id is mandatory and doubles
        // as the idempotency key (PayPal stores it 6h), so a duplicate create returns the same order.
        // return=representation so the response carries the authorization the card create already placed.
        Order created;
        try
        {
            created = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                // Unique per attempt (the invoice id carries a fresh GUID) so PayPal does not replay a
                // prior run's cached create; double-click idempotency is enforced by the DB claim upstream.
                payPalRequestId: request.InvoiceId,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: orderRequest,
                prefer: "return=representation",
                ct: deadline);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw Translate("create-order", ex.Error.TryGetError(out var e) ? e : null,
                ex.Error.TryGetRawError(out var raw) ? raw : null, ex);
        }
        catch (Exception ex) when (IsConnectionFailure(ex, ct))
        {
            throw Connection("create-order", ex);
        }

        var payPalOrderId = created.Id
            ?? throw new PayPalGatewayException("PayPal did not return an order id.", PaymentGatewayErrorKind.Unavailable);

        // Passing a card at create with intent=AUTHORIZE authorizes in one step, so the hold is usually
        // already on the created order. Only if it is not do we call AuthorizeOrder explicitly (e.g. a
        // flow that created the order without the payment source).
        var authorization = ExtractAuthorization(created.PurchaseUnits);
        if (authorization?.Id is null)
        {
            OrderAuthorizeResponse authorized;
            try
            {
                authorized = await _client.Orders.AuthorizeOrder(
                    id: payPalOrderId,
                    payPalMockResponse: null,
                    payPalRequestId: $"{request.InvoiceId}-authorize",
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: null,
                    prefer: "return=representation",
                    ct: deadline);
            }
            catch (SdkException<AuthorizeOrderError> ex)
            {
                throw Translate("authorize-order", ex.Error.TryGetError(out var e) ? e : null,
                    ex.Error.TryGetRawError(out var raw) ? raw : null, ex, payPalOrderId);
            }
            catch (Exception ex) when (IsConnectionFailure(ex, ct))
            {
                throw Connection("authorize-order", ex, payPalOrderId);
            }

            authorization = ExtractAuthorization(authorized.PurchaseUnits);
        }

        if (authorization?.Id is null)
        {
            // No authorization came back — the order may need shopper approval (a 3DS challenge), which
            // this integration deliberately does not perform.
            throw new PayPalGatewayException(
                "PayPal did not create an authorization for this card. If the card requires shopper approval " +
                "(3-D Secure), this integration does not support that flow.",
                PaymentGatewayErrorKind.ApprovalRequired);
        }

        var status = authorization.Status?.Value ?? "UNKNOWN";
        if (status == AuthorizationStatus.Denied.Value)
        {
            throw new PayPalGatewayException(
                "The card was declined by PayPal.", PaymentGatewayErrorKind.Validation, issue: status);
        }

        var amount = CurrencyFormatter.TryParse(authorization.Amount?.Value) ?? request.Amount;
        return new AuthorizationResult(payPalOrderId, authorization.Id, status, amount,
            authorization.Amount?.CurrencyCode ?? request.Currency, authorization.ExpirationTime);
    }

    public async Task<CaptureResult> CaptureAsync(string authorizationId, string currency, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);

        CapturedPayment captured;
        try
        {
            captured = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: null,
                payPalAuthAssertion: null,
                body: new CaptureRequest { FinalCapture = true },
                prefer: "return=representation",
                ct: cts.Token);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            var raw = (ex.Error.TryGetNoContent(out var noContent) ? noContent : null)
                      ?? (ex.Error.TryGetRawError(out var r) ? r : null);
            throw Translate("capture", ex.Error.TryGetError(out var e) ? e : null, raw, ex);
        }
        catch (Exception ex) when (IsConnectionFailure(ex, ct))
        {
            // The capture may have landed — surface as Unknown so the caller re-reads (GetOrder).
            throw Connection("capture", ex, unknownWrite: true);
        }
        catch (JsonException ex)
        {
            throw new PayPalGatewayException(
                "PayPal returned an unreadable capture response; the outcome is unknown.",
                PaymentGatewayErrorKind.Unknown, inner: ex);
        }

        return MapCapture(captured.Id, captured.Status?.Value, captured.Amount,
            captured.SellerReceivableBreakdown, currency);
    }

    public async Task<AuthorizationStatusResult> GetAuthorizationAsync(string authorizationId, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        try
        {
            var auth = await _client.Payments.GetAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                ct: cts.Token);
            return new AuthorizationStatusResult(auth.Status?.Value ?? "UNKNOWN", auth.ExpirationTime);
        }
        catch (SdkException<GetAuthorizedPaymentError> ex)
        {
            var raw = (ex.Error.TryGetNoContent(out var noContent) ? noContent : null)
                      ?? (ex.Error.TryGetRawError(out var r) ? r : null);
            throw Translate("get-authorization", ex.Error.TryGetError(out var e) ? e : null, raw, ex);
        }
        catch (Exception ex) when (IsConnectionFailure(ex, ct))
        {
            throw Connection("get-authorization", ex);
        }
    }

    public async Task<ReauthorizeResult> ReauthorizeAsync(string authorizationId, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        try
        {
            var reauth = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: null,
                payPalAuthAssertion: null,
                body: null,
                prefer: "return=representation",
                ct: cts.Token);

            var newId = reauth.Id ?? authorizationId;
            return new ReauthorizeResult(newId, reauth.Status?.Value ?? "UNKNOWN", reauth.ExpirationTime);
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            // Any reauthorization failure means the hold cannot be renewed — actionable for an operator.
            var (_, issue, debugId) = ReadError(ex.Error.TryGetError(out var e) ? e : null,
                (ex.Error.TryGetNoContent(out var nc) ? nc : null) ?? (ex.Error.TryGetRawError(out var r) ? r : null));
            _logger.LogWarning("PayPal reauthorization failed for {AuthorizationId}: issue={Issue} debug_id={DebugId}",
                authorizationId, issue, debugId);
            throw new PayPalGatewayException(
                "The payment authorization has expired and could not be renewed. Ask the shopper to pay again.",
                PaymentGatewayErrorKind.AuthorizationNotRenewable, issue: issue, debugId: debugId, inner: ex);
        }
        catch (Exception ex) when (IsConnectionFailure(ex, ct))
        {
            throw Connection("reauthorize", ex);
        }
    }

    public async Task VoidAsync(string authorizationId, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        try
        {
            await _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: null,
                prefer: "return=minimal",
                ct: cts.Token);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            var raw = (ex.Error.TryGetNoContent(out var noContent) ? noContent : null)
                      ?? (ex.Error.TryGetRawError(out var r) ? r : null);
            throw Translate("void", ex.Error.TryGetError(out var e) ? e : null, raw, ex);
        }
        catch (Exception ex) when (IsConnectionFailure(ex, ct))
        {
            throw Connection("void", ex);
        }
    }

    public async Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);

        var body = amount is null
            ? null
            : new RefundRequest { Amount = new Money { CurrencyCode = currency, Value = CurrencyFormatter.Format(amount.Value, currency) } };

        try
        {
            // PayPal-Request-Id dedups a genuine retry of THIS refund. PayPal's request-id namespace is
            // account-global, so it is scoped to the capture — otherwise the same caller key reused across
            // two different captures would collide. App-level double-refund is already blocked by the
            // (OrderId, IdempotencyKey) claim before this call.
            var refund = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: $"{captureId}-{idempotencyKey}",
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: cts.Token);

            var refundedAmount = CurrencyFormatter.TryParse(refund.Amount?.Value) ?? amount ?? 0m;
            return new RefundResult(refund.Id ?? string.Empty, refund.Status?.Value ?? "UNKNOWN",
                refundedAmount, refund.Amount?.CurrencyCode ?? currency);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            var raw = (ex.Error.TryGetNoContent(out var noContent) ? noContent : null)
                      ?? (ex.Error.TryGetRawError(out var r) ? r : null);
            throw Translate("refund", ex.Error.TryGetError(out var e) ? e : null, raw, ex);
        }
        catch (Exception ex) when (IsConnectionFailure(ex, ct))
        {
            throw Connection("refund", ex, unknownWrite: true);
        }
    }

    public async Task<CaptureLookupResult?> FindCaptureByPayPalOrderAsync(string payPalOrderId, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        try
        {
            var order = await _client.Orders.GetOrder(
                id: payPalOrderId,
                fields: null,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                ct: cts.Token);

            var capture = order.PurchaseUnits?
                .SelectMany(pu => pu.Payments?.Captures ?? new List<OrdersCapture>())
                .FirstOrDefault(c => c.Id is not null);

            if (capture?.Id is null) return null;

            var mapped = MapCapture(capture.Id, capture.Status?.Value, capture.Amount,
                capture.SellerReceivableBreakdown, Currency);
            return new CaptureLookupResult(mapped.CaptureId, mapped.Status, mapped.GrossAmount,
                mapped.PayPalFee, mapped.NetAmount, mapped.Currency);
        }
        catch (SdkException<GetOrderError> ex)
        {
            throw Translate("get-order", ex.Error.TryGetError(out var e) ? e : null,
                ex.Error.TryGetRawError(out var raw) ? raw : null, ex);
        }
        catch (Exception ex) when (IsConnectionFailure(ex, ct))
        {
            throw Connection("get-order", ex);
        }
    }

    public async Task<VaultCardResult> VaultCardAsync(VaultCardRequest request, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);

        var customerForSetup = request.ExistingCustomerId is not null
            ? new Customer { Id = request.ExistingCustomerId }
            : new Customer { MerchantCustomerId = request.MerchantCustomerId };

        // Two-step browserless vault: a setup token holds the raw card briefly, then a payment token is
        // created from it. Vaulting a raw card directly at /v3/vault/payment-tokens is not reliable.
        string setupTokenId;
        string? customerId = request.ExistingCustomerId;
        try
        {
            var setup = await _client.Vault.CreateSetupToken(
                payPalRequestId: null,
                body: new SetupTokenRequest
                {
                    Customer = customerForSetup,
                    PaymentSource = new SetupTokenRequestPaymentSource
                    {
                        Card = new SetupTokenRequestCard
                        {
                            Number = request.Card.Number,
                            Expiry = request.Card.ExpiryYearMonth,
                            Name = request.Card.CardholderName,
                            SecurityCode = request.Card.SecurityCode,
                            BillingAddress = BuildAddress(request.Card.BillingAddress)
                        }
                    }
                },
                ct: cts.Token);
            setupTokenId = setup.Id
                ?? throw new PayPalGatewayException("PayPal did not return a setup token id.", PaymentGatewayErrorKind.Unavailable);
            customerId = setup.Customer?.Id ?? customerId;
        }
        catch (SdkException<CreateSetupTokenError> ex)
        {
            throw Translate("create-setup-token", ex.Error.TryGetError(out var e) ? e : null,
                ex.Error.TryGetRawError(out var raw) ? raw : null, ex);
        }
        catch (Exception ex) when (IsConnectionFailure(ex, ct))
        {
            throw Connection("create-setup-token", ex);
        }

        try
        {
            var token = await _client.Vault.CreatePaymentToken(
                payPalRequestId: null,
                body: new PaymentTokenRequest
                {
                    Customer = customerId is not null ? new Customer { Id = customerId } : null,
                    PaymentSource = new PaymentTokenRequestPaymentSource
                    {
                        Token = new VaultTokenRequest { Id = setupTokenId, Type = VaultTokenRequestType.SetupToken }
                    }
                },
                ct: cts.Token);

            var vaultId = token.Id
                ?? throw new PayPalGatewayException("PayPal did not return a vault token id.", PaymentGatewayErrorKind.Unavailable);
            var cardEntity = token.PaymentSource?.Card;
            return new VaultCardResult(
                vaultId,
                token.Customer?.Id ?? customerId,
                cardEntity?.Brand?.Value ?? "UNKNOWN",
                cardEntity?.LastDigits ?? Last4(request.Card.Number),
                cardEntity?.Expiry ?? request.Card.ExpiryYearMonth,
                cardEntity?.Name ?? request.Card.CardholderName);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            throw Translate("vault-card", ex.Error.TryGetError(out var e) ? e : null,
                ex.Error.TryGetRawError(out var raw) ? raw : null, ex);
        }
        catch (Exception ex) when (IsConnectionFailure(ex, ct))
        {
            throw Connection("vault-card", ex);
        }
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        try
        {
            await _client.Vault.DeletePaymentToken(id: vaultId, ct: cts.Token);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            // A token already gone is fine — deletion is idempotent from the app's perspective.
            if (ex.Error.TryGetError(out var e) && IsNotFound(e))
            {
                _logger.LogInformation("Vault token {VaultId} already absent at PayPal.", vaultId);
                return;
            }
            throw Translate("delete-vault", ex.Error.TryGetError(out var e2) ? e2 : null,
                ex.Error.TryGetRawError(out var raw) ? raw : null, ex);
        }
        catch (Exception ex) when (IsConnectionFailure(ex, ct))
        {
            throw Connection("delete-vault", ex);
        }
    }

    public async Task<ReconciliationSearchResult> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct)
    {
        var transactions = new List<ReconciliationTransaction>();
        var pagesScanned = 0;
        var totalItems = 0;
        var truncated = false;
        var windows = 0;

        var windowStart = from;
        while (windowStart < to)
        {
            if (++windows > MaxWindows)
            {
                truncated = true;
                _logger.LogWarning("Reconciliation window cap ({MaxWindows}) reached; result truncated.", MaxWindows);
                break;
            }

            var windowEnd = windowStart + MaxSearchWindow;
            if (windowEnd > to) windowEnd = to;

            var page = 1;
            int totalPages;
            do
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(CallBudget);

                SearchResponse response;
                try
                {
                    response = await _client.TransactionSearch.SearchTransactions(
                        startDate: FormatSearchDate(windowStart),
                        endDate: FormatSearchDate(windowEnd),
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
                        pageSize: SearchPageSize,
                        page: page,
                        ct: cts.Token);
                }
                catch (SdkException<RawError> ex) // TransactionSearch is Case B
                {
                    throw Translate("search-transactions", ex.Error, ex);
                }
                catch (Exception ex) when (IsConnectionFailure(ex, ct))
                {
                    throw Connection("search-transactions", ex);
                }

                pagesScanned++;
                totalPages = response.TotalPages ?? 1;
                totalItems += page == 1 ? (response.TotalItems ?? 0) : 0;

                foreach (var detail in response.TransactionDetails ?? new List<TransactionDetails>())
                {
                    var info = detail.TransactionInfo;
                    if (info?.TransactionId is null) continue;
                    transactions.Add(new ReconciliationTransaction(
                        info.TransactionId,
                        string.IsNullOrEmpty(info.InvoiceId) ? null : info.InvoiceId,
                        CurrencyFormatter.TryParse(info.TransactionAmount?.Value),
                        info.TransactionAmount?.CurrencyCode,
                        info.TransactionStatus,
                        info.TransactionInitiationDate));
                }

                if (page >= MaxPagesPerWindow && page < totalPages)
                {
                    truncated = true;
                    _logger.LogWarning("Reconciliation page cap ({MaxPages}) reached within a window; result truncated.",
                        MaxPagesPerWindow);
                    break;
                }
                page++;
            }
            while (page <= totalPages);

            windowStart = windowEnd;
        }

        return new ReconciliationSearchResult(transactions, pagesScanned, totalItems, truncated);
    }

    // ---- helpers ----

    private static AuthorizationWithAdditionalData? ExtractAuthorization(IReadOnlyList<PurchaseUnit>? units) =>
        units?
            .SelectMany(pu => pu.Payments?.Authorizations ?? new List<AuthorizationWithAdditionalData>())
            .FirstOrDefault(a => a.Id is not null);

    private static string FormatSearchDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string Last4(string number) =>
        number.Length >= 4 ? number.Substring(number.Length - 4) : number;

    private static CardRequest BuildCardRequest(CardDetails? card, string? vaultId)
    {
        if (vaultId is not null)
        {
            return new CardRequest { VaultId = vaultId };
        }
        if (card is null)
        {
            throw new PayPalGatewayException("No card or saved payment method supplied.", PaymentGatewayErrorKind.Validation);
        }
        return new CardRequest
        {
            Number = card.Number,
            Expiry = card.ExpiryYearMonth,
            SecurityCode = card.SecurityCode,
            Name = card.CardholderName,
            BillingAddress = BuildAddress(card.BillingAddress)
        };
    }

    private static Address? BuildAddress(CardBillingAddress? a)
    {
        if (a is null) return null;
        // CountryCode is required on the SDK Address; default when a billing address is given without one.
        return new Address
        {
            AddressLine1 = a.AddressLine1,
            AdminArea2 = a.AdminArea2,
            AdminArea1 = a.AdminArea1,
            PostalCode = a.PostalCode,
            CountryCode = string.IsNullOrWhiteSpace(a.CountryCode) ? "US" : a.CountryCode!.Trim().ToUpperInvariant()
        };
    }

    private CaptureResult MapCapture(string? id, string? status, Money? amount,
        SellerReceivableBreakdown? breakdown, string fallbackCurrency)
    {
        var captureId = id
            ?? throw new PayPalGatewayException("PayPal did not return a capture id.", PaymentGatewayErrorKind.Unavailable);
        var gross = CurrencyFormatter.TryParse(breakdown?.GrossAmount?.Value)
                    ?? CurrencyFormatter.TryParse(amount?.Value) ?? 0m;
        var fee = CurrencyFormatter.TryParse(breakdown?.PaypalFee?.Value);
        var net = CurrencyFormatter.TryParse(breakdown?.NetAmount?.Value);
        var currency = breakdown?.GrossAmount?.CurrencyCode ?? amount?.CurrencyCode ?? fallbackCurrency;
        return new CaptureResult(captureId, status ?? "UNKNOWN", gross, fee, net, currency);
    }

    private static bool IsNotFound(Error error) =>
        error.Name.Contains("NOT_FOUND", StringComparison.OrdinalIgnoreCase);

    private static bool IsConnectionFailure(Exception ex, CancellationToken callerToken) =>
        ex is HttpRequestException
        || (ex is TaskCanceledException or OperationCanceledException && !callerToken.IsCancellationRequested);

    private (string message, string? issue, string? debugId) ReadError(Error? typed, RawError? raw)
    {
        if (typed is not null)
        {
            var issue = typed.Details is { Count: > 0 } ? typed.Details[0].Issue : typed.Name;
            var message = typed.Details is { Count: > 0 } && !string.IsNullOrEmpty(typed.Details[0].Description)
                ? typed.Details[0].Description!
                : typed.Message;
            return (message, issue, typed.DebugId);
        }
        if (raw is not null)
        {
            return ($"PayPal returned HTTP {(int)raw.StatusCode}.", null, null);
        }
        return ("PayPal returned an error.", null, null);
    }

    private PayPalGatewayException Translate(string op, Error? typed, RawError? raw, Exception inner,
        string? payPalOrderIdForContext = null)
    {
        var (message, issue, debugId) = ReadError(typed, raw);
        var kind = ClassifyKind(typed, raw, issue);
        _logger.LogWarning(
            "PayPal {Op} failed: kind={Kind} issue={Issue} debug_id={DebugId} status={Status} ppOrder={PayPalOrder}",
            op, kind, issue, debugId, raw is null ? null : (int?)raw.StatusCode, payPalOrderIdForContext);
        return new PayPalGatewayException(CallerSafeMessage(kind, message), kind,
            raw is null ? null : (int)raw.StatusCode, issue, debugId, inner);
    }

    // Case B (RawError only): TransactionSearch.
    private PayPalGatewayException Translate(string op, RawError raw, Exception inner)
    {
        var kind = ClassifyFromStatus((int)raw.StatusCode);
        _logger.LogWarning("PayPal {Op} failed: kind={Kind} status={Status}", op, kind, (int)raw.StatusCode);
        return new PayPalGatewayException(CallerSafeMessage(kind, $"PayPal returned HTTP {(int)raw.StatusCode}."),
            kind, (int)raw.StatusCode, null, null, inner);
    }

    private PayPalGatewayException Connection(string op, Exception inner, string? payPalOrderIdForContext = null,
        bool unknownWrite = false)
    {
        var kind = unknownWrite ? PaymentGatewayErrorKind.Unknown : PaymentGatewayErrorKind.Unavailable;
        _logger.LogWarning(inner, "PayPal {Op} transport failure (kind={Kind}, ppOrder={PayPalOrder}).",
            op, kind, payPalOrderIdForContext);
        var message = unknownWrite
            ? "PayPal could not be reached and the outcome of this operation is unknown."
            : "PayPal is currently unavailable. Please try again.";
        return new PayPalGatewayException(message, kind, inner: inner);
    }

    private static PaymentGatewayErrorKind ClassifyKind(Error? typed, RawError? raw, string? issue)
    {
        if (issue is not null)
        {
            if (issue.Contains("PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase) ||
                issue.Contains("3D", StringComparison.OrdinalIgnoreCase) && issue.Contains("SECURE", StringComparison.OrdinalIgnoreCase))
                return PaymentGatewayErrorKind.ApprovalRequired;
            if (issue.Contains("ALREADY_CAPTURED", StringComparison.OrdinalIgnoreCase) ||
                issue.Contains("ALREADY_VOIDED", StringComparison.OrdinalIgnoreCase) ||
                issue.Contains("ALREADY_REFUNDED", StringComparison.OrdinalIgnoreCase) ||
                issue.Contains("ORDER_ALREADY", StringComparison.OrdinalIgnoreCase) ||
                issue.Contains("AUTHORIZATION_ALREADY", StringComparison.OrdinalIgnoreCase))
                return PaymentGatewayErrorKind.Conflict;
        }

        if (raw is not null) return ClassifyFromStatus((int)raw.StatusCode);

        var name = typed?.Name ?? string.Empty;
        if (name.Contains("NOT_FOUND", StringComparison.OrdinalIgnoreCase)) return PaymentGatewayErrorKind.NotFound;
        if (name.Contains("CONFLICT", StringComparison.OrdinalIgnoreCase)) return PaymentGatewayErrorKind.Conflict;
        if (name.Contains("AUTHENTICATION", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("NOT_AUTHORIZED", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("PERMISSION_DENIED", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("INTERNAL", StringComparison.OrdinalIgnoreCase))
            return PaymentGatewayErrorKind.Unavailable;
        // INVALID_REQUEST, UNPROCESSABLE_ENTITY, INSTRUMENT_DECLINED, etc. — caller can act on it.
        return PaymentGatewayErrorKind.Validation;
    }

    private static PaymentGatewayErrorKind ClassifyFromStatus(int status) => status switch
    {
        400 or 422 => PaymentGatewayErrorKind.Validation,
        404 => PaymentGatewayErrorKind.NotFound,
        409 => PaymentGatewayErrorKind.Conflict,
        401 or 403 or 429 => PaymentGatewayErrorKind.Unavailable,
        >= 500 => PaymentGatewayErrorKind.Unavailable,
        _ => PaymentGatewayErrorKind.Validation
    };

    private static string CallerSafeMessage(PaymentGatewayErrorKind kind, string providerMessage) => kind switch
    {
        PaymentGatewayErrorKind.Validation => providerMessage,
        PaymentGatewayErrorKind.NotFound => providerMessage,
        PaymentGatewayErrorKind.Conflict => providerMessage,
        PaymentGatewayErrorKind.ApprovalRequired =>
            "The card requires shopper approval (3-D Secure), which is not supported.",
        PaymentGatewayErrorKind.Unavailable => "PayPal is currently unavailable. Please try again.",
        PaymentGatewayErrorKind.Unknown => "The outcome of the operation is unknown.",
        _ => "PayPal could not process the request."
    };
}
