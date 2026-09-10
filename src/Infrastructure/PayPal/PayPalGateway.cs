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
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// The PayPal Server SDK adapter. Translates the application's payment requests into SDK calls and the
/// SDK's models/failures back into the application's own types. Nothing above this class touches the SDK.
///
/// Error boundary (per the SDK error-handling contract): every operation is Case A (typed
/// <c>{Operation}Error</c>) except transaction search, which is Case B (<c>RawError</c>). A drifted 2xx or
/// a non-2xx body that does not match the generated error shape surfaces as <see cref="JsonException"/>,
/// which is caught here; connection failures come through as <see cref="HttpRequestException"/> /
/// <see cref="TaskCanceledException"/>. All become <see cref="PayPalPaymentException"/>.
/// </summary>
public class PayPalGateway : IPayPalGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(60);
    private const int MaxTransactionPages = 1000;

    private readonly PayPalServerSdkClient _client;
    private readonly IPaymentConfiguration _configuration;
    private readonly IAppLogger<PayPalGateway> _logger;

    public PayPalGateway(
        PayPalServerSdkClient client,
        IPaymentConfiguration configuration,
        IAppLogger<PayPalGateway> logger)
    {
        _client = client;
        _configuration = configuration;
        _logger = logger;
    }

    private string Currency => _configuration.Currency;

    public async Task<AuthorizationResult> AuthorizeAsync(
        decimal amount,
        string correlationId,
        CardPaymentInstrument instrument,
        string idempotencyKey,
        CancellationToken ct)
    {
        using var scope = Bound(ct);
        var token = scope.Token;

        // Direct-card AUTHORIZE flow: the card is supplied at order creation so PayPal processes it,
        // then the authorization (the hold) is created. With the card present the order is processed in
        // the create call; if PayPal returns it merely APPROVED we complete it with an explicit authorize.
        var createBody = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new()
                {
                    Amount = new AmountWithBreakdown { CurrencyCode = Currency, Value = Format(amount) },
                    // custom_id correlates the transaction to the eShop order for reconciliation. We do
                    // NOT set invoice_id: the merchant account enforces invoice_id uniqueness, and the
                    // in-memory store reuses order ids across runs.
                    CustomId = correlationId
                }
            },
            PaymentSource = BuildCreatePaymentSource(instrument)
        };

        Order created = await CreateOrderCall(createBody, idempotencyKey, token);
        var payPalOrderId = created.Id
            ?? throw new PayPalPaymentException("PayPal did not return an order id on create.");
        ThrowIfChallenge(created.Status);

        var authorization = ExtractAuthorization(created.PurchaseUnits);
        var status = created.Status?.Value;

        if (authorization?.Id is null)
        {
            // Card processed but not yet authorized (order APPROVED): create the authorization.
            OrderAuthorizeResponse authorized = await AuthorizeOrderCall(payPalOrderId, null, idempotencyKey, token);
            ThrowIfChallenge(authorized.Status);
            authorization = ExtractAuthorization(authorized.PurchaseUnits);
            status = authorized.Status?.Value ?? status;
        }

        if (authorization?.Id is null)
            throw new PayPalPaymentException(
                $"PayPal did not return an authorization for order {payPalOrderId} (status {status ?? "unknown"}).");

        return new AuthorizationResult(
            payPalOrderId,
            authorization.Id,
            authorization.Status?.Value ?? status ?? "CREATED",
            ParseMoney(authorization.Amount) ?? amount,
            authorization.Amount?.CurrencyCode ?? Currency);
    }

    public async Task<AuthorizationRenewalResult> EnsureCapturableAsync(
        string authorizationId,
        decimal amount,
        string idempotencyKey,
        CancellationToken ct)
    {
        using var scope = Bound(ct);
        var token = scope.Token;

        PaymentAuthorization auth = await GetAuthorizedPaymentCall(authorizationId, token);
        var status = auth.Status?.Value;

        if (status == AuthorizationStatus.Captured.Value)
            return new AuthorizationRenewalResult(false, authorizationId, false, "The authorization has already been captured.");
        if (status == AuthorizationStatus.Voided.Value)
            return new AuthorizationRenewalResult(false, authorizationId, false, "The authorization was voided.");
        if (status == AuthorizationStatus.Denied.Value)
            return new AuthorizationRenewalResult(false, authorizationId, false, "The authorization was denied.");

        if (!IsExpired(auth.ExpirationTime))
            return new AuthorizationRenewalResult(false, authorizationId, true, null);

        // Stale hold — try to renew it rather than failing the fulfilment outright.
        try
        {
            var reauth = await ReauthorizeCall(
                authorizationId, new ReauthorizeRequest { Amount = new Money { CurrencyCode = Currency, Value = Format(amount) } },
                idempotencyKey, token);
            var newId = reauth.Id ?? authorizationId;
            _logger.LogWarning("Reauthorized stale hold {0} to {1}.", authorizationId, newId);
            return new AuthorizationRenewalResult(true, newId, true, null);
        }
        catch (PayPalPaymentException ex)
        {
            return new AuthorizationRenewalResult(false, authorizationId, false,
                $"The authorization expired and can no longer be renewed ({ex.Message}). A new authorization is required.");
        }
    }

    public async Task<CaptureResult> CaptureAsync(
        string authorizationId,
        decimal amount,
        string correlationId,
        string idempotencyKey,
        CancellationToken ct)
    {
        using var scope = Bound(ct);
        var body = new CaptureRequest
        {
            Amount = new Money { CurrencyCode = Currency, Value = Format(amount) },
            FinalCapture = true
        };

        CapturedPayment capture = await CaptureCall(authorizationId, body, idempotencyKey, scope.Token);
        if (capture.Id is null)
            throw new PayPalPaymentException("PayPal did not return a capture id.");

        var breakdown = capture.SellerReceivableBreakdown;
        var gross = ParseMoney(breakdown?.GrossAmount) ?? ParseMoney(capture.Amount) ?? amount;
        return new CaptureResult(
            capture.Id,
            capture.Status?.Value ?? "COMPLETED",
            gross,
            ParseMoney(breakdown?.PaypalFee),
            ParseMoney(breakdown?.NetAmount),
            capture.Amount?.CurrencyCode ?? Currency);
    }

    public async Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct)
    {
        using var scope = Bound(ct);
        await VoidCall(authorizationId, idempotencyKey, scope.Token);
    }

    public async Task<RefundResult> RefundAsync(
        string captureId,
        decimal? amount,
        string idempotencyKey,
        CancellationToken ct)
    {
        using var scope = Bound(ct);
        var body = amount is null
            ? new RefundRequest()
            : new RefundRequest { Amount = new Money { CurrencyCode = Currency, Value = Format(amount.Value) } };

        Refund refund = await RefundCall(captureId, body, idempotencyKey, scope.Token);
        if (refund.Id is null)
            throw new PayPalPaymentException("PayPal did not return a refund id.");

        return new RefundResult(
            refund.Id,
            refund.Status?.Value ?? "COMPLETED",
            ParseMoney(refund.Amount) ?? amount ?? 0m,
            refund.Amount?.CurrencyCode ?? Currency);
    }

    public async Task<SavedCardResult> VaultCardAsync(
        string merchantCustomerId,
        CardDetails card,
        string idempotencyKey,
        CancellationToken ct)
    {
        using var scope = Bound(ct);
        var body = new PaymentTokenRequest
        {
            Customer = new Customer { MerchantCustomerId = merchantCustomerId },
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    Name = card.Name,
                    BillingAddress = BuildAddress(card.BillingAddress)
                }
            }
        };

        PaymentTokenResponse response = await CreatePaymentTokenCall(body, idempotencyKey, scope.Token);
        if (response.Id is null)
            throw new PayPalPaymentException("PayPal did not return a vault token id.");

        var cardEntity = response.PaymentSource?.Card;
        return new SavedCardResult(
            response.Id,
            response.Customer?.Id ?? merchantCustomerId,
            cardEntity?.Brand?.Value,
            cardEntity?.LastDigits,
            cardEntity?.Expiry);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct)
    {
        using var scope = Bound(ct);
        await DeletePaymentTokenCall(vaultId, scope.Token);
    }

    public async Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct)
    {
        using var scope = Bound(ct);
        var token = scope.Token;
        var startDate = FormatDate(from);
        var endDate = FormatDate(to);

        var records = new List<PayPalTransactionRecord>();
        int page = 1;
        int totalPages;
        do
        {
            SearchResponse response = await SearchTransactionsCall(startDate, endDate, page, token);
            foreach (var detail in response.TransactionDetails ?? new List<TransactionDetails>())
            {
                var info = detail.TransactionInfo;
                if (info is null) continue;
                records.Add(new PayPalTransactionRecord(
                    info.TransactionId,
                    info.CustomField ?? info.InvoiceId,
                    ParseMoney(info.TransactionAmount),
                    info.TransactionAmount?.CurrencyCode,
                    info.TransactionStatus,
                    ParseDate(info.TransactionInitiationDate)));
            }

            totalPages = response.TotalPages ?? 1;
            page++;
        }
        while (page <= totalPages && page <= MaxTransactionPages);

        return records;
    }

    // --- SDK call wrappers: each encapsulates one operation's error boundary ---------------------------

    private async Task<Order> CreateOrderCall(OrderRequest body, string key, CancellationToken ct)
    {
        try
        {
            return await _client.Orders.CreateOrder(
                payPalMockResponse: null, payPalRequestId: key, payPalPartnerAttributionId: null,
                payPalClientMetadataId: null, payPalAuthAssertion: null, body: body,
                prefer: "return=representation", ct: ct);
        }
        catch (SdkException<CreateOrderError> ex) { throw Translate(ex, ex.Error.TryGetError(out var e) ? e : null, "create order"); }
        catch (Exception ex) when (IsInfrastructure(ex, ct)) { throw Infrastructure(ex, ct); }
    }

    private async Task<OrderAuthorizeResponse> AuthorizeOrderCall(string id, OrderAuthorizeRequest body, string key, CancellationToken ct)
    {
        try
        {
            return await _client.Orders.AuthorizeOrder(
                id: id, payPalMockResponse: null, payPalRequestId: key, payPalClientMetadataId: null,
                payPalAuthAssertion: null, body: body, prefer: "return=representation", ct: ct);
        }
        catch (SdkException<AuthorizeOrderError> ex) { throw Translate(ex, ex.Error.TryGetError(out var e) ? e : null, "authorize order"); }
        catch (Exception ex) when (IsInfrastructure(ex, ct)) { throw Infrastructure(ex, ct); }
    }

    private async Task<PaymentAuthorization> GetAuthorizedPaymentCall(string authorizationId, CancellationToken ct)
    {
        try
        {
            return await _client.Payments.GetAuthorizedPayment(
                authorizationId: authorizationId, payPalMockResponse: null, payPalAuthAssertion: null, ct: ct);
        }
        catch (SdkException<GetAuthorizedPaymentError> ex) { throw Translate(ex, ex.Error.TryGetError(out var e) ? e : null, "get authorization"); }
        catch (Exception ex) when (IsInfrastructure(ex, ct)) { throw Infrastructure(ex, ct); }
    }

    private async Task<PaymentAuthorization> ReauthorizeCall(string authorizationId, ReauthorizeRequest body, string key, CancellationToken ct)
    {
        try
        {
            return await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId, payPalRequestId: key, payPalAuthAssertion: null,
                body: body, prefer: "return=representation", ct: ct);
        }
        catch (SdkException<ReauthorizePaymentError> ex) { throw Translate(ex, ex.Error.TryGetError(out var e) ? e : null, "reauthorize"); }
        catch (Exception ex) when (IsInfrastructure(ex, ct)) { throw Infrastructure(ex, ct); }
    }

    private async Task<CapturedPayment> CaptureCall(string authorizationId, CaptureRequest body, string key, CancellationToken ct)
    {
        try
        {
            return await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId, payPalMockResponse: null, payPalRequestId: key,
                payPalAuthAssertion: null, body: body, prefer: "return=representation", ct: ct);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex) { throw Translate(ex, ex.Error.TryGetError(out var e) ? e : null, "capture"); }
        catch (Exception ex) when (IsInfrastructure(ex, ct)) { throw Infrastructure(ex, ct); }
    }

    private async Task VoidCall(string authorizationId, string key, CancellationToken ct)
    {
        try
        {
            await _client.Payments.VoidPayment(
                authorizationId: authorizationId, payPalMockResponse: null, payPalAuthAssertion: null,
                payPalRequestId: key, prefer: "return=minimal", ct: ct);
        }
        catch (SdkException<VoidPaymentError> ex) { throw Translate(ex, ex.Error.TryGetError(out var e) ? e : null, "void"); }
        // A successful void returns 204 No Content; the SDK's deserialization of the empty body throws
        // JsonException. That is success for void, not a failure — swallow it.
        catch (JsonException) { }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            throw Infrastructure(ex, ct);
        }
    }

    private async Task<Refund> RefundCall(string captureId, RefundRequest body, string key, CancellationToken ct)
    {
        try
        {
            return await _client.Payments.RefundCapturedPayment(
                captureId: captureId, payPalMockResponse: null, payPalRequestId: key, payPalAuthAssertion: null,
                body: body, prefer: "return=representation", ct: ct);
        }
        catch (SdkException<RefundCapturedPaymentError> ex) { throw Translate(ex, ex.Error.TryGetError(out var e) ? e : null, "refund"); }
        catch (Exception ex) when (IsInfrastructure(ex, ct)) { throw Infrastructure(ex, ct); }
    }

    private async Task<PaymentTokenResponse> CreatePaymentTokenCall(PaymentTokenRequest body, string key, CancellationToken ct)
    {
        try
        {
            return await _client.Vault.CreatePaymentToken(payPalRequestId: key, body: body, ct: ct);
        }
        catch (SdkException<CreatePaymentTokenError> ex) { throw Translate(ex, ex.Error.TryGetError(out var e) ? e : null, "vault card"); }
        catch (Exception ex) when (IsInfrastructure(ex, ct)) { throw Infrastructure(ex, ct); }
    }

    private async Task DeletePaymentTokenCall(string id, CancellationToken ct)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(id: id, ct: ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex) { throw Translate(ex, ex.Error.TryGetError(out var e) ? e : null, "delete saved card"); }
        catch (Exception ex) when (IsInfrastructure(ex, ct)) { throw Infrastructure(ex, ct); }
    }

    private async Task<SearchResponse> SearchTransactionsCall(string startDate, string endDate, int page, CancellationToken ct)
    {
        try
        {
            return await _client.TransactionSearch.SearchTransactions(
                startDate: startDate, endDate: endDate, transactionId: null, transactionType: null,
                transactionStatus: null, transactionAmount: null, transactionCurrency: null,
                paymentInstrumentType: null, storeId: null, terminalId: null,
                fields: "transaction_info", balanceAffectingRecordsOnly: "Y", pageSize: 100, page: page, ct: ct);
        }
        // Transaction search is the SDK's only Case B operation — RawError, no typed accessors.
        catch (SdkException<RawError> ex)
        {
            _logger.LogWarning("PayPal transaction search failed: HTTP {0}.", (int)ex.Error.StatusCode);
            throw new PayPalPaymentException(
                $"PayPal transaction search failed (HTTP {(int)ex.Error.StatusCode}).", statusCode: ex.Error.StatusCode, innerException: ex);
        }
        catch (Exception ex) when (IsInfrastructure(ex, ct)) { throw Infrastructure(ex, ct); }
    }

    // --- Error translation ----------------------------------------------------------------------------

    private PayPalPaymentException Translate<TErr>(SdkException<TErr> ex, Error? typed, string operation)
        where TErr : ApiError
    {
        if (typed is not null)
        {
            var issues = typed.Details is { Count: > 0 }
                ? string.Join("; ", typed.Details.Select(d =>
                    $"{d.Issue}{(string.IsNullOrEmpty(d.Field) ? "" : $" [{d.Field}]")}{(string.IsNullOrEmpty(d.Description) ? "" : $": {d.Description}")}"))
                : null;
            var detail = issues is null ? typed.Message : $"{typed.Message} ({issues})";
            _logger.LogWarning("PayPal {0} failed: {1} — {2} (debug_id {3}).", operation, typed.Name, detail, typed.DebugId);
            return new PayPalPaymentException($"PayPal {operation} failed: {detail}", typed.DebugId, innerException: ex);
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            _logger.LogWarning("PayPal {0} failed: HTTP {1}.", operation, (int)raw.StatusCode);
            return new PayPalPaymentException($"PayPal {operation} failed (HTTP {(int)raw.StatusCode}).", statusCode: raw.StatusCode, innerException: ex);
        }
        _logger.LogWarning("PayPal {0} failed with an unrecognised error shape.", operation);
        return new PayPalPaymentException($"PayPal {operation} failed with an unrecognised error.", innerException: ex);
    }

    // Connection failures and drifted/malformed bodies (a JsonException reaches here from a 2xx that no
    // longer matches its model, or a non-2xx whose body did not match the generated error shape).
    private static bool IsInfrastructure(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException
        || ex is JsonException
        || (ex is TaskCanceledException && !ct.IsCancellationRequested);

    private PayPalPaymentException Infrastructure(Exception ex, CancellationToken ct)
    {
        if (ex is JsonException)
        {
            _logger.LogWarning("PayPal returned a response that could not be processed: {0}.", ex.Message);
            return new PayPalPaymentException("PayPal returned a response that could not be processed.", innerException: ex);
        }
        if (ex is TaskCanceledException)
            return new PayPalPaymentException("The PayPal request timed out.", innerException: ex);
        return new PayPalPaymentException("PayPal is currently unreachable.", innerException: ex);
    }

    // --- Helpers --------------------------------------------------------------------------------------

    private CancellationTokenSource Bound(CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return cts;
    }

    private PaymentSource BuildCreatePaymentSource(CardPaymentInstrument instrument) =>
        new() { Card = BuildCardRequest(instrument) };

    private CardRequest BuildCardRequest(CardPaymentInstrument instrument)
    {
        if (!string.IsNullOrEmpty(instrument.VaultId))
            return new CardRequest { VaultId = instrument.VaultId };

        var c = instrument.Card
            ?? throw new PayPalPaymentException("No card or saved card was supplied for the payment.");
        return new CardRequest
        {
            Number = c.Number,
            Expiry = c.Expiry,
            SecurityCode = c.SecurityCode,
            Name = c.Name,
            BillingAddress = BuildAddress(c.BillingAddress)
        };
    }

    private static void ThrowIfChallenge(OrderStatus? status)
    {
        if (status is not null && status.Value == OrderStatus.PayerActionRequired.Value)
            throw new PayPalChallengeException(
                "This card requires the shopper to approve the payment in a browser (PAYER_ACTION_REQUIRED). " +
                "This unbranded card integration does not support an approval round-trip.");
    }

    private static AuthorizationWithAdditionalData? ExtractAuthorization(IReadOnlyList<PurchaseUnit>? purchaseUnits) =>
        purchaseUnits?
            .SelectMany(pu => pu.Payments?.Authorizations ?? new List<AuthorizationWithAdditionalData>())
            .FirstOrDefault(a => a.Id is not null);

    private static Address? BuildAddress(BillingAddressInput? input)
    {
        // Address.CountryCode is required; without it we omit the billing address (optional for the test card).
        if (input is null || string.IsNullOrWhiteSpace(input.CountryCode))
            return null;
        return new Address
        {
            CountryCode = input.CountryCode!,
            AddressLine1 = input.AddressLine1,
            AddressLine2 = input.AddressLine2,
            AdminArea1 = input.AdminArea1,
            AdminArea2 = input.AdminArea2,
            PostalCode = input.PostalCode
        };
    }

    private static string Format(decimal value) => value.ToString("F2", CultureInfo.InvariantCulture);

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static decimal? ParseMoney(Money? money) =>
        money is null ? null : decimal.Parse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt) ? dt : null;

    private static bool IsExpired(string? expirationTime)
    {
        if (DateTimeOffset.TryParse(expirationTime, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expiry))
            return expiry <= DateTimeOffset.UtcNow;
        return false; // if we can't read it, don't force a reauthorization
    }
}
