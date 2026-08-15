using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using Sdk = PayPalServerSdk.Models;
using SdkEnums = PayPalServerSdk.Models.Enums;
using SdkErrors = PayPalServerSdk.Errors;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// PayPal implementation of <see cref="IPaymentGateway"/>. All PayPal-specific detail — the SDK client,
/// model shapes, enum values, error translation, money formatting and pagination — is confined here.
/// </summary>
public class PayPalPaymentGateway : IPaymentGateway
{
    private const string PreferRepresentation = "return=representation";
    private readonly PayPalServerSdkClient _client;
    private readonly ILogger<PayPalPaymentGateway> _logger;

    // A per-process id mixed into every PayPal-Request-Id. Idempotency keys are deterministic per order
    // (order-{id}-authorize, etc.), but with the in-memory database those order ids reset to 1 on every
    // restart — and this app's data lives exactly one process lifetime. Scoping the request id to the
    // process keeps a double-click deduped within a run (the key stays stable) while never colliding with a
    // previous run or another instance that shares the same PayPal account. (In a persistent-database
    // deployment the order id alone is globally unique, so this simply becomes a harmless constant suffix.)
    private readonly string _instanceId = Guid.NewGuid().ToString("N").Substring(0, 12);

    public PayPalPaymentGateway(PayPalServerSdkClient client, ILogger<PayPalPaymentGateway> logger)
    {
        _client = client;
        _logger = logger;
    }

    private string RequestId(string key) => $"{key}-{_instanceId}";

    // ---------- Authorize (create order + authorize, direct card or vaulted card) ----------

    public async Task<AuthorizationResult> AuthorizeAsync(AuthorizationRequest request, CancellationToken cancellationToken = default)
    {
        var card = request.VaultTokenId is not null
            ? new Sdk.CardRequest { VaultId = request.VaultTokenId }
            : BuildCard(request.Card!);

        var orderRequest = new Sdk.OrderRequest
        {
            Intent = SdkEnums.CheckoutPaymentIntent.Authorize,
            PaymentSource = new Sdk.PaymentSource { Card = card },
            PurchaseUnits = new List<Sdk.PurchaseUnitRequest>
            {
                new Sdk.PurchaseUnitRequest
                {
                    ReferenceId = request.OrderReference,
                    CustomId = request.OrderReference, // correlates PayPal transactions back to the eShop order
                    Amount = new Sdk.AmountWithBreakdown
                    {
                        CurrencyCode = request.Currency,
                        Value = FormatMoney(request.Amount)
                    }
                }
            }
        };

        Sdk.Order created;
        try
        {
            created = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: RequestId(request.IdempotencyKey),
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: orderRequest,
                prefer: PreferRepresentation,
                ct: cancellationToken);
        }
        catch (SdkException<SdkErrors.CreateOrderError> ex)
        {
            ex.Error.TryGetError(out var err);
            ex.Error.TryGetRawError(out var raw);
            throw Fail("create the payment", err, raw, ex);
        }
        catch (Exception ex) when (IsTransport(ex)) { throw Unreachable(ex); }
        catch (JsonException ex) { throw Unreadable(ex); }

        RequireNotPayerAction(created.Status, created.Id);

        var (authId, authStatus) = ReadAuthorization(created.PurchaseUnits);

        // A create with an inline card + intent=AUTHORIZE may already carry the authorization; if not,
        // authorize explicitly.
        if (authId is null)
        {
            Sdk.OrderAuthorizeResponse authorized;
            try
            {
                authorized = await _client.Orders.AuthorizeOrder(
                    id: created.Id,
                    payPalMockResponse: null,
                    payPalRequestId: RequestId($"{request.IdempotencyKey}-auth"),
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: null,
                    prefer: PreferRepresentation,
                    ct: cancellationToken);
            }
            catch (SdkException<SdkErrors.AuthorizeOrderError> ex)
            {
                ex.Error.TryGetError(out var err);
                ex.Error.TryGetRawError(out var raw);
                throw Fail("authorize the payment", err, raw, ex);
            }
            catch (Exception ex) when (IsTransport(ex)) { throw Unreachable(ex); }
            catch (JsonException ex) { throw Unreadable(ex); }

            RequireNotPayerAction(authorized.Status, created.Id);
            (authId, authStatus) = ReadAuthorization(authorized.PurchaseUnits);
        }

        if (authId is null)
            throw new PaymentGatewayException("PayPal did not return a payment authorization for the order.");

        return new AuthorizationResult
        {
            PayPalOrderId = created.Id,
            AuthorizationId = authId,
            Status = authStatus ?? "CREATED"
        };
    }

    // ---------- Capture (fulfilment) ----------

    public async Task<CaptureResult> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Sdk.CapturedPayment captured;
        try
        {
            captured = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: RequestId(idempotencyKey),
                payPalAuthAssertion: null,
                body: null, // full capture of the authorized amount
                prefer: PreferRepresentation,
                ct: cancellationToken);
        }
        catch (SdkException<SdkErrors.CaptureAuthorizedPaymentError> ex)
        {
            ex.Error.TryGetError(out var err);
            ex.Error.TryGetRawError(out var raw);
            if (LooksExpired(err))
                throw new AuthorizationExpiredException(Describe(err, raw, "The authorization has expired."), err?.DebugId, ex);
            throw Fail("capture the payment", err, raw, ex);
        }
        catch (Exception ex) when (IsTransport(ex)) { throw Unreachable(ex); }
        catch (JsonException ex) { throw Unreadable(ex); }

        var breakdown = captured.SellerReceivableBreakdown;
        return new CaptureResult
        {
            CaptureId = captured.Id,
            Status = EnumWire(captured.Status) ?? "COMPLETED",
            GrossAmount = ParseMoney(breakdown?.GrossAmount?.Value) ?? ParseMoney(captured.Amount?.Value) ?? 0m,
            PayPalFee = ParseMoney(breakdown?.PaypalFee?.Value) ?? 0m,
            NetAmount = ParseMoney(breakdown?.NetAmount?.Value) ?? 0m,
            Currency = breakdown?.GrossAmount?.CurrencyCode ?? captured.Amount?.CurrencyCode ?? string.Empty
        };
    }

    // ---------- Reauthorize (renew a stale hold) ----------

    public async Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency, CancellationToken cancellationToken = default)
    {
        Sdk.PaymentAuthorization reauthorized;
        try
        {
            reauthorized = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: null,
                payPalAuthAssertion: null,
                body: new Sdk.ReauthorizeRequest { Amount = BuildMoney(amount, currency) },
                prefer: PreferRepresentation,
                ct: cancellationToken);
        }
        catch (SdkException<SdkErrors.ReauthorizePaymentError> ex)
        {
            ex.Error.TryGetError(out var err);
            ex.Error.TryGetRawError(out var raw);
            // A reauthorization that PayPal rejects cannot be renewed — surface it in operator terms.
            throw new PaymentGatewayException(
                Describe(err, raw, "The payment authorization has expired and can no longer be renewed; ask the shopper to pay again."),
                isOperatorActionable: true, debugId: err?.DebugId, inner: ex);
        }
        catch (Exception ex) when (IsTransport(ex)) { throw Unreachable(ex); }
        catch (JsonException ex) { throw Unreadable(ex); }

        return new AuthorizationResult
        {
            PayPalOrderId = string.Empty,
            AuthorizationId = reauthorized.Id,
            Status = EnumWire(reauthorized.Status) ?? "CREATED"
        };
    }

    // ---------- Void (cancel before capture) ----------

    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: null,
                prefer: "return=minimal",
                ct: cancellationToken);
        }
        catch (SdkException<SdkErrors.VoidPaymentError> ex)
        {
            ex.Error.TryGetError(out var err);
            ex.Error.TryGetRawError(out var raw);
            throw Fail("release the held funds", err, raw, ex);
        }
        catch (Exception ex) when (IsTransport(ex)) { throw Unreachable(ex); }
        catch (JsonException)
        {
            // A successful void returns HTTP 204 with an empty body; the SDK then throws JsonException
            // trying to deserialize "no content". The hold was released — treat it as success.
            _logger.LogInformation("Void of {AuthorizationId} returned an empty body (204); treating as released.", authorizationId);
        }
    }

    // ---------- Refund (full or partial, after capture) ----------

    public async Task<RefundResult> RefundAsync(RefundRequest request, CancellationToken cancellationToken = default)
    {
        Sdk.Refund refund;
        try
        {
            refund = await _client.Payments.RefundCapturedPayment(
                captureId: request.CaptureId,
                payPalMockResponse: null,
                payPalRequestId: RequestId(request.IdempotencyKey),
                payPalAuthAssertion: null,
                body: request.Amount is decimal amt
                    ? new Sdk.RefundRequest { Amount = BuildMoney(amt, request.Currency) }
                    : null,
                prefer: PreferRepresentation,
                ct: cancellationToken);
        }
        catch (SdkException<SdkErrors.RefundCapturedPaymentError> ex)
        {
            ex.Error.TryGetError(out var err);
            ex.Error.TryGetRawError(out var raw);
            throw Fail("refund the payment", err, raw, ex);
        }
        catch (Exception ex) when (IsTransport(ex)) { throw Unreachable(ex); }
        catch (JsonException ex) { throw Unreadable(ex); }

        return new RefundResult
        {
            RefundId = refund.Id,
            Status = EnumWire(refund.Status) ?? "COMPLETED",
            Amount = ParseMoney(refund.Amount?.Value) ?? request.Amount ?? 0m
        };
    }

    // ---------- Vault a card ----------

    public async Task<VaultedCardResult> VaultCardAsync(CardDetails card, string customerId, CancellationToken cancellationToken = default)
    {
        Sdk.PaymentTokenResponse token;
        try
        {
            token = await _client.Vault.CreatePaymentToken(
                payPalRequestId: null,
                body: new Sdk.PaymentTokenRequest
                {
                    Customer = new Sdk.Customer { MerchantCustomerId = customerId },
                    PaymentSource = new Sdk.PaymentTokenRequestPaymentSource
                    {
                        Card = new Sdk.PaymentTokenRequestCard
                        {
                            Name = card.CardholderName,
                            Number = card.Number,
                            Expiry = FormatExpiry(card),
                            SecurityCode = card.SecurityCode,
                            BillingAddress = BuildAddress(card.BillingAddress)
                        }
                    }
                },
                ct: cancellationToken);
        }
        catch (SdkException<SdkErrors.CreatePaymentTokenError> ex)
        {
            ex.Error.TryGetError1(out var err);
            ex.Error.TryGetRawError(out var raw);
            throw Fail("save the card", err, raw, ex);
        }
        catch (Exception ex) when (IsTransport(ex)) { throw Unreachable(ex); }
        catch (JsonException ex) { throw Unreadable(ex); }

        var vaultedCard = token.PaymentSource?.Card;
        var (month, year) = SplitExpiry(vaultedCard?.Expiry) is var e ? e : (null, null);
        return new VaultedCardResult
        {
            VaultTokenId = token.Id,
            CardBrand = EnumWire(vaultedCard?.Brand),
            Last4 = vaultedCard?.LastDigits,
            ExpiryMonth = month,
            ExpiryYear = year
        };
    }

    public async Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(id: vaultTokenId, ct: cancellationToken);
        }
        catch (SdkException<SdkErrors.DeletePaymentTokenError> ex)
        {
            // Already gone is fine — the goal (no longer usable) is met either way.
            ex.Error.TryGetRawError(out var raw);
            if (raw is not null && (int)raw.StatusCode == 404)
                return;
            ex.Error.TryGetError1(out var err);
            throw Fail("remove the saved card", err, raw, ex);
        }
        catch (Exception ex) when (IsTransport(ex)) { throw Unreachable(ex); }
        catch (JsonException ex) { throw Unreadable(ex); }
    }

    // ---------- Transaction search (reconciliation) — all pages ----------

    public async Task<IReadOnlyList<GatewayTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        const int pageSize = 100;
        const int maxPages = 2000; // safety cap; PayPal caps the window itself
        var results = new List<GatewayTransaction>();
        var startDate = FormatDate(from);
        var endDate = FormatDate(to);

        int page = 1;
        int totalPages = 1;
        do
        {
            Sdk.SearchResponse response;
            try
            {
                response = await _client.TransactionSearch.SearchTransactions(
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
                    ct: cancellationToken);
            }
            catch (SdkException<RawError> ex)
            {
                throw new PaymentGatewayException(
                    $"PayPal rejected the reconciliation query (HTTP {(int)ex.Error.StatusCode}).", inner: ex);
            }
            catch (Exception ex) when (IsTransport(ex)) { throw Unreachable(ex); }
            catch (JsonException ex) { throw Unreadable(ex); }

            var details = response.TransactionDetails;
            if (details is not null)
            {
                foreach (var d in details)
                {
                    var info = d.TransactionInfo;
                    if (info is null) continue;
                    results.Add(new GatewayTransaction
                    {
                        TransactionId = info.TransactionId ?? string.Empty,
                        Status = info.TransactionStatus,
                        Amount = ParseMoney(info.TransactionAmount?.Value),
                        Currency = info.TransactionAmount?.CurrencyCode,
                        ReferenceId = info.CustomField ?? info.InvoiceId,
                        Date = ParseDate(info.TransactionInitiationDate)
                    });
                }
            }

            totalPages = response.TotalPages ?? 1;
            if (details is null || details.Count == 0) break;
            page++;
        }
        while (page <= totalPages && page <= maxPages);

        if (totalPages > maxPages)
            _logger.LogWarning("Reconciliation truncated at {MaxPages} pages of {TotalPages}.", maxPages, totalPages);

        return results;
    }

    // ---------- helpers ----------

    private static Sdk.CardRequest BuildCard(CardDetails card) => new()
    {
        Name = card.CardholderName,
        Number = card.Number,
        Expiry = FormatExpiry(card),
        SecurityCode = card.SecurityCode,
        BillingAddress = BuildAddress(card.BillingAddress)
    };

    private static Sdk.Address? BuildAddress(BillingAddress? address) => address is null ? null : new Sdk.Address
    {
        AddressLine1 = address.AddressLine1,
        AddressLine2 = address.AddressLine2,
        AdminArea2 = address.AdminArea2,
        AdminArea1 = address.AdminArea1,
        PostalCode = address.PostalCode,
        CountryCode = address.CountryCode
    };

    private static (string? authId, string? status) ReadAuthorization(IReadOnlyList<Sdk.PurchaseUnit>? units)
    {
        var authorization = units?
            .FirstOrDefault()?.Payments?
            .Authorizations?.FirstOrDefault();
        return (authorization?.Id, EnumWire(authorization?.Status));
    }

    private static void RequireNotPayerAction(SdkEnums.OrderStatus? status, string orderId)
    {
        if (status == SdkEnums.OrderStatus.PayerActionRequired)
            throw new PaymentRequiresCustomerActionException(
                $"PayPal requires the shopper to approve this card payment in a browser (order {orderId}).");
    }

    private static string FormatMoney(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static Sdk.Money BuildMoney(decimal amount, string currency) =>
        new() { CurrencyCode = currency, Value = FormatMoney(amount) };

    private static decimal? ParseMoney(string? value) =>
        decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static string FormatExpiry(CardDetails card)
    {
        var month = card.ExpiryMonth.PadLeft(2, '0');
        return $"{card.ExpiryYear}-{month}"; // wire format YYYY-MM
    }

    private static (string? month, string? year) SplitExpiry(string? expiry)
    {
        if (string.IsNullOrEmpty(expiry)) return (null, null);
        var parts = expiry.Split('-');
        return parts.Length == 2 ? (parts[1], parts[0]) : (null, null);
    }

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d) ? d : null;

    private static bool IsTransport(Exception ex) => ex is HttpRequestException or TaskCanceledException;

    private PaymentGatewayException Unreachable(Exception inner)
    {
        _logger.LogError(inner, "PayPal was unreachable.");
        return new PaymentGatewayException("The payment provider is currently unreachable. Please try again.", inner: inner);
    }

    private PaymentGatewayException Unreadable(Exception inner)
    {
        _logger.LogError(inner, "PayPal returned an unreadable response.");
        return new PaymentGatewayException("The payment provider returned a response that could not be processed.", inner: inner);
    }

    private PaymentGatewayException Fail(string action, Sdk.Error? err, RawError? raw, Exception inner)
    {
        var message = Describe(err, raw, $"PayPal could not {action}.");
        _logger.LogError(inner, "PayPal failed to {Action}. DebugId={DebugId}", action, err?.DebugId);
        return new PaymentGatewayException(message, debugId: err?.DebugId, inner: inner);
    }

    private static string Describe(Sdk.Error? err, RawError? raw, string fallback)
    {
        if (err is not null)
        {
            var detail = err.Details?.FirstOrDefault();
            var issue = detail?.Issue;
            var description = detail?.Description ?? err.Message;
            if (!string.IsNullOrEmpty(description))
                return issue is null ? description : $"{description} ({issue})";
            if (!string.IsNullOrEmpty(err.Message))
                return err.Message;
        }
        return fallback;
    }

    // Vault operations surface the typed payload as Error1 (accessor TryGetError1), which is
    // structurally identical to Error — same Message/DebugId/Details[].Issue/Description shape.
    private PaymentGatewayException Fail(string action, Sdk.Error1? err, RawError? raw, Exception inner)
    {
        var message = Describe(err, raw, $"PayPal could not {action}.");
        _logger.LogError(inner, "PayPal failed to {Action}. DebugId={DebugId}", action, err?.DebugId);
        return new PaymentGatewayException(message, debugId: err?.DebugId, inner: inner);
    }

    private static string Describe(Sdk.Error1? err, RawError? raw, string fallback)
    {
        if (err is not null)
        {
            var detail = err.Details?.FirstOrDefault();
            var issue = detail?.Issue;
            var description = detail?.Description ?? err.Message;
            if (!string.IsNullOrEmpty(description))
                return issue is null ? description : $"{description} ({issue})";
            if (!string.IsNullOrEmpty(err.Message))
                return err.Message;
        }
        return fallback;
    }

    private static bool LooksExpired(Sdk.Error? err)
    {
        if (err is null) return false;
        var text = (err.Message ?? string.Empty) + " " +
                   string.Join(" ", err.Details?.Select(d => $"{d.Issue} {d.Description}") ?? Enumerable.Empty<string>());
        text = text.ToUpperInvariant();
        // Only a genuinely expired hold is renewable. A voided/denied authorization is not — reauthorizing
        // it would be wrong, so match expiry alone.
        return text.Contains("EXPIRE");
    }

    /// <summary>
    /// Reads the wire value out of an SDK <c>StringEnum</c> (whose ToString renders as
    /// "Type { Value = WIRE }"), so stored/displayed statuses are clean values like "CREATED".
    /// </summary>
    private static string? EnumWire(object? value)
    {
        if (value is null) return null;
        var s = value.ToString();
        if (string.IsNullOrEmpty(s)) return s;
        const string marker = "Value = ";
        var idx = s.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) return s;
        var start = idx + marker.Length;
        var end = s.IndexOf('}', start);
        if (end < 0) end = s.Length;
        return s.Substring(start, end - start).Trim();
    }
}
