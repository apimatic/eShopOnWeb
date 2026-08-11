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
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// The only place in the app that talks to PayPal, via the apimatic PayPal .NET SDK. Translates the
/// app's gateway operations into SDK calls and the SDK's failures into caller-safe
/// <see cref="PaymentGatewayException"/>s (never leaking SDK/framework detail), classifying a genuine
/// client rejection (4xx) apart from an outage (transport / 5xx).
/// </summary>
public class PayPalPaymentGateway : IPayPalPaymentGateway
{
    private const string PreferRepresentation = "return=representation";

    private readonly PayPalServerSdkClient _client;
    private readonly ILogger<PayPalPaymentGateway> _logger;
    private readonly TimeSpan _callBudget = TimeSpan.FromSeconds(40);

    public PayPalPaymentGateway(PayPalServerSdkClient client, ILogger<PayPalPaymentGateway> logger)
    {
        _client = client;
        _logger = logger;
    }

    // --- Authorize -----------------------------------------------------------

    public async Task<PayPalAuthorizationResult> AuthorizeAsync(
        decimal amount, string currency, string referenceId,
        RawCard? card, string? vaultId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (card is null == (vaultId is null))
        {
            throw new ArgumentException("Exactly one of card or vaultId must be supplied.");
        }

        using var scope = Bounded(cancellationToken, out var ct);

        var body = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    ReferenceId = referenceId,
                    CustomId = referenceId,
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = currency,
                        Value = Format(amount)
                    }
                }
            },
            PaymentSource = new PaymentSource
            {
                Card = card is not null
                    ? new CardRequest
                    {
                        Number = card.Number,
                        Expiry = card.Expiry,
                        SecurityCode = card.SecurityCode,
                        Name = card.Name,
                        BillingAddress = ToAddress(card.BillingAddress)
                    }
                    : new CardRequest { VaultId = vaultId }
            }
        };

        try
        {
            var created = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: $"create-{idempotencyKey}",
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: body,
                prefer: PreferRepresentation,
                ct: ct);

            var paypalOrderId = created.Id
                ?? throw new PaymentGatewayException("PayPal did not return an order id.");

            // With intent=AUTHORIZE and a card, the authorization may already be on the create
            // response; if not, explicitly authorize the order. Detect a browser-approval challenge.
            var auth = ExtractAuthorization(created.PurchaseUnits);
            if (auth is null)
            {
                GuardNotAwaitingApproval(created.Status?.Value, created.Links);

                var authorized = await _client.Orders.AuthorizeOrder(
                    id: paypalOrderId,
                    payPalMockResponse: null,
                    payPalRequestId: $"authorize-{idempotencyKey}",
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: null,
                    prefer: PreferRepresentation,
                    ct: ct);

                auth = ExtractAuthorization(authorized.PurchaseUnits);
                if (auth is null)
                {
                    GuardNotAwaitingApproval(authorized.Status?.Value, authorized.Links);
                    throw new PaymentGatewayException("PayPal did not return an authorization for the order.");
                }
            }

            var info = auth.Value;
            return new PayPalAuthorizationResult(paypalOrderId, info.Id, info.Status, info.ExpiresAt);
        }
        catch (SdkException<CreateOrderError> ex) { throw TranslateCreateOrder("authorize the order", ex); }
        catch (SdkException<AuthorizeOrderError> ex) { throw TranslateAuthorizeOrder("authorize the order", ex); }
        catch (Exception ex) when (IsTransport(ex)) { throw Transport("authorize the order", ex); }
        catch (JsonException ex) { throw Parse("authorize the order", ex); }
    }

    // --- Capture -------------------------------------------------------------

    public async Task<PayPalCaptureResult> CaptureAsync(
        string authorizationId, decimal amount, string currency, string referenceId,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        using var scope = Bounded(cancellationToken, out var ct);

        var body = new CaptureRequest { FinalCapture = true };

        try
        {
            var captured = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: $"capture-{idempotencyKey}",
                payPalAuthAssertion: null,
                body: body,
                prefer: PreferRepresentation,
                ct: ct);

            var captureId = captured.Id
                ?? throw new PaymentGatewayException("PayPal did not return a capture id.");
            var status = captured.Status?.Value ?? "COMPLETED";

            var breakdown = captured.SellerReceivableBreakdown;
            var gross = ParseMoney(breakdown?.GrossAmount) ?? ParseMoney(captured.Amount) ?? amount;
            var fee = ParseMoney(breakdown?.PaypalFee);
            var net = ParseMoney(breakdown?.NetAmount);

            return new PayPalCaptureResult(captureId, status, gross, fee, net, currency);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex) { throw TranslateCapture("capture the payment", ex); }
        catch (Exception ex) when (IsTransport(ex)) { throw Transport("capture the payment", ex); }
        catch (JsonException ex) { throw Parse("capture the payment", ex); }
    }

    // --- Reauthorize ---------------------------------------------------------

    public async Task<PayPalReauthorizationResult> ReauthorizeAsync(
        string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        using var scope = Bounded(cancellationToken, out var ct);

        var body = new ReauthorizeRequest
        {
            Amount = new Money { CurrencyCode = currency, Value = Format(amount) }
        };

        try
        {
            var reauth = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: $"reauth-{idempotencyKey}",
                payPalAuthAssertion: null,
                body: body,
                prefer: PreferRepresentation,
                ct: ct);

            var newId = reauth.Id
                ?? throw new AuthorizationNotRenewableException(
                    "The authorization could not be renewed; place the payment again on a new order.");
            var status = reauth.Status?.Value ?? "CREATED";
            var expires = ParseDate(reauth.ExpirationTime);
            return new PayPalReauthorizationResult(newId, status, expires);
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            // A 4xx here means the hold can no longer be renewed — say so in operator terms.
            var translated = TranslateReauthorize("renew the authorization", ex);
            if (translated.ProviderStatusCode is >= 400 and < 500)
            {
                throw new AuthorizationNotRenewableException(
                    "The authorization has expired and can no longer be renewed. Ask the shopper to pay the order again.",
                    translated.ProviderStatusCode, translated.DebugId, ex);
            }
            throw translated;
        }
        catch (Exception ex) when (IsTransport(ex)) { throw Transport("renew the authorization", ex); }
        catch (JsonException ex) { throw Parse("renew the authorization", ex); }
    }

    // --- Void ----------------------------------------------------------------

    public async Task VoidAsync(
        string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        using var scope = Bounded(cancellationToken, out var ct);

        try
        {
            await _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: $"void-{idempotencyKey}",
                prefer: PreferRepresentation,
                ct: ct);
        }
        catch (SdkException<VoidPaymentError> ex) { throw TranslateVoid("release the hold", ex); }
        catch (Exception ex) when (IsTransport(ex)) { throw Transport("release the hold", ex); }
        catch (JsonException)
        {
            // A successful void is a 204 No Content; the SDK throws JsonException deserializing the
            // empty body. A real rejection would have surfaced as SdkException<VoidPaymentError> above,
            // so an empty-body result here means the hold was released.
            _logger.LogInformation("Void of authorization {AuthorizationId} returned no content (released).", authorizationId);
        }
    }

    // --- Refund --------------------------------------------------------------

    public async Task<PayPalRefundResult> RefundAsync(
        string captureId, decimal? amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        using var scope = Bounded(cancellationToken, out var ct);

        RefundRequest? body = amount is null
            ? null
            : new RefundRequest { Amount = new Money { CurrencyCode = currency, Value = Format(amount.Value) } };

        try
        {
            var refund = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey, // caller-supplied idempotency key
                payPalAuthAssertion: null,
                body: body,
                prefer: PreferRepresentation,
                ct: ct);

            var refundId = refund.Id
                ?? throw new PaymentGatewayException("PayPal did not return a refund id.");
            var status = refund.Status?.Value ?? "COMPLETED";
            var refundedAmount = ParseMoney(refund.Amount) ?? amount ?? 0m;
            return new PayPalRefundResult(refundId, status, refundedAmount);
        }
        catch (SdkException<RefundCapturedPaymentError> ex) { throw TranslateRefund("refund the payment", ex); }
        catch (Exception ex) when (IsTransport(ex)) { throw Transport("refund the payment", ex); }
        catch (JsonException ex) { throw Parse("refund the payment", ex); }
    }

    // --- Vault ---------------------------------------------------------------

    public async Task<PayPalVaultResult> VaultCardAsync(RawCard card, CancellationToken cancellationToken = default)
    {
        using var scope = Bounded(cancellationToken, out var ct);

        var body = new PaymentTokenRequest
        {
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    Name = card.Name,
                    BillingAddress = ToAddress(card.BillingAddress)
                }
            }
        };

        try
        {
            var token = await _client.Vault.CreatePaymentToken(
                payPalRequestId: null,
                body: body,
                ct: ct);

            var tokenId = token.Id
                ?? throw new PaymentGatewayException("PayPal did not return a vault token id.");

            var cardInfo = token.PaymentSource?.Card;
            var brand = cardInfo?.Brand?.Value ?? "CARD";
            var last4 = cardInfo?.LastDigits ?? Last4Of(card.Number);
            var expiry = cardInfo?.Expiry ?? card.Expiry;

            return new PayPalVaultResult(tokenId, brand, last4, expiry);
        }
        catch (SdkException<CreatePaymentTokenError> ex) { throw TranslateVault("save the card", ex); }
        catch (Exception ex) when (IsTransport(ex)) { throw Transport("save the card", ex); }
        catch (JsonException ex) { throw Parse("save the card", ex); }
    }

    public async Task DeleteVaultedCardAsync(string tokenId, CancellationToken cancellationToken = default)
    {
        using var scope = Bounded(cancellationToken, out var ct);

        try
        {
            await _client.Vault.DeletePaymentToken(id: tokenId, ct: ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex) { throw TranslateVaultDelete("remove the saved card", ex); }
        catch (Exception ex) when (IsTransport(ex)) { throw Transport("remove the saved card", ex); }
        catch (JsonException ex) { throw Parse("remove the saved card", ex); }
    }

    // --- Reconciliation ------------------------------------------------------

    public async Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        using var scope = Bounded(cancellationToken, out var ct);

        var results = new List<PayPalTransactionRecord>();
        var startDate = FormatDate(from);
        var endDate = FormatDate(to);

        int page = 1;
        int totalPages;
        do
        {
            SearchResponse response;
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
                    pageSize: 100,
                    page: page,
                    ct: ct);
            }
            catch (SdkException<RawError> ex) { throw TranslateRaw("read PayPal transactions", ex.Error); }
            catch (Exception ex) when (IsTransport(ex)) { throw Transport("read PayPal transactions", ex); }
            catch (JsonException ex) { throw Parse("read PayPal transactions", ex); }

            foreach (var detail in response.TransactionDetails ?? Enumerable.Empty<TransactionDetails>())
            {
                var info = detail.TransactionInfo;
                if (info?.TransactionId is null)
                {
                    continue;
                }

                results.Add(new PayPalTransactionRecord(
                    TransactionId: info.TransactionId,
                    Amount: ParseMoney(info.TransactionAmount),
                    Currency: info.TransactionAmount?.CurrencyCode,
                    Status: info.TransactionStatus,
                    Date: ParseDate(info.TransactionInitiationDate),
                    InvoiceId: info.InvoiceId,
                    CustomField: info.CustomField));
            }

            totalPages = response.TotalPages ?? 1;
            page++;
        }
        while (page <= totalPages);

        return results;
    }

    // --- Helpers -------------------------------------------------------------

    private readonly record struct AuthInfo(string Id, string Status, DateTimeOffset? ExpiresAt);

    private static AuthInfo? ExtractAuthorization(IReadOnlyList<PurchaseUnit>? purchaseUnits)
    {
        if (purchaseUnits is null)
        {
            return null;
        }

        foreach (var unit in purchaseUnits)
        {
            var authorization = unit.Payments?.Authorizations?.FirstOrDefault(a => a.Id is not null);
            if (authorization?.Id is not null)
            {
                return new AuthInfo(
                    authorization.Id,
                    authorization.Status?.Value ?? "CREATED",
                    ParseDate(authorization.ExpirationTime));
            }
        }

        return null;
    }

    private void GuardNotAwaitingApproval(string? status, IReadOnlyList<LinkDescription>? links)
    {
        var needsApproval =
            (status is not null && status.Contains("PAYER_ACTION", StringComparison.OrdinalIgnoreCase)) ||
            (links?.Any(l =>
                (l.Rel?.Contains("payer-action", StringComparison.OrdinalIgnoreCase) ?? false) ||
                (l.Rel?.Contains("approve", StringComparison.OrdinalIgnoreCase) ?? false)) ?? false);

        if (needsApproval)
        {
            _logger.LogWarning("PayPal returned a browser-approval challenge for a card payment (status {Status}).", status);
            throw new PaymentApprovalRequiredException(
                "PayPal requires the shopper to approve this card payment in a browser, which this integration does not support.");
        }
    }

    private static string Format(decimal amount) =>
        amount.ToString("F2", CultureInfo.InvariantCulture);

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    private static string Last4Of(string number)
    {
        var digits = new string(number.Where(char.IsDigit).ToArray());
        return digits.Length >= 4 ? digits[^4..] : digits;
    }

    private static decimal? ParseMoney(Money? money) =>
        money?.Value is { } v && decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
            ? d
            : null;

    private static DateTimeOffset? ParseDate(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
            ? dt
            : null;

    private static Address? ToAddress(CardBillingAddress? a) =>
        a is null
            ? null
            : new Address
            {
                AddressLine1 = a.AddressLine1,
                AddressLine2 = a.AddressLine2,
                AdminArea2 = a.AdminArea2,
                AdminArea1 = a.AdminArea1,
                PostalCode = a.PostalCode,
                CountryCode = a.CountryCode
            };

    /// <summary>
    /// Bounds the whole call (across the SDK's internal retries) with a total budget linked to the
    /// caller's cancellation, and returns a scope to dispose. See dotnet-configuration-resilience.
    /// </summary>
    private CancellationTokenSourceScope Bounded(CancellationToken caller, out CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(caller);
        cts.CancelAfter(_callBudget);
        ct = cts.Token;
        return new CancellationTokenSourceScope(cts);
    }

    private readonly struct CancellationTokenSourceScope : IDisposable
    {
        private readonly CancellationTokenSource _cts;
        public CancellationTokenSourceScope(CancellationTokenSource cts) => _cts = cts;
        public void Dispose() => _cts.Dispose();
    }

    private static bool IsTransport(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or OperationCanceledException;

    // Error translation. Each catch reads the operation's typed accessor(s) first, then the RawError
    // fallback last (it is not a catch-all). A typed body = a client-actionable 4xx; RawError carries
    // the real status. No SDK/framework message is ever surfaced — only a caller-safe sentence.

    private PaymentGatewayException TranslateCreateOrder(string action, SdkException<CreateOrderError> ex)
    {
        if (ex.Error.TryGetError(out Error e)) return Client(action, e, 400);
        if (ex.Error.TryGetRawError(out RawError raw)) return TranslateRaw(action, raw);
        return Unknown(action);
    }

    private PaymentGatewayException TranslateAuthorizeOrder(string action, SdkException<AuthorizeOrderError> ex)
    {
        if (ex.Error.TryGetError(out Error e)) return Client(action, e, 400);
        if (ex.Error.TryGetRawError(out RawError raw)) return TranslateRaw(action, raw);
        return Unknown(action);
    }

    private PaymentGatewayException TranslateCapture(string action, SdkException<CaptureAuthorizedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out Error e)) return Client(action, e, 422);
        if (ex.Error.TryGetNoContent(out RawError noContent)) return Server(action, noContent);
        if (ex.Error.TryGetRawError(out RawError raw)) return TranslateRaw(action, raw);
        return Unknown(action);
    }

    private PaymentGatewayException TranslateReauthorize(string action, SdkException<ReauthorizePaymentError> ex)
    {
        if (ex.Error.TryGetError(out Error e)) return Client(action, e, 422);
        if (ex.Error.TryGetNoContent(out RawError noContent)) return Server(action, noContent);
        if (ex.Error.TryGetRawError(out RawError raw)) return TranslateRaw(action, raw);
        return Unknown(action);
    }

    private PaymentGatewayException TranslateVoid(string action, SdkException<VoidPaymentError> ex)
    {
        if (ex.Error.TryGetError(out Error e)) return Client(action, e, 422);
        if (ex.Error.TryGetNoContent(out RawError noContent)) return Server(action, noContent);
        if (ex.Error.TryGetRawError(out RawError raw)) return TranslateRaw(action, raw);
        return Unknown(action);
    }

    private PaymentGatewayException TranslateRefund(string action, SdkException<RefundCapturedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out Error e)) return Client(action, e, 422);
        if (ex.Error.TryGetNoContent(out RawError noContent)) return Server(action, noContent);
        if (ex.Error.TryGetRawError(out RawError raw)) return TranslateRaw(action, raw);
        return Unknown(action);
    }

    private PaymentGatewayException TranslateVault(string action, SdkException<CreatePaymentTokenError> ex)
    {
        if (ex.Error.TryGetError1(out Error1 e)) return Client1(action, e, 422);
        if (ex.Error.TryGetRawError(out RawError raw)) return TranslateRaw(action, raw);
        return Unknown(action);
    }

    private PaymentGatewayException TranslateVaultDelete(string action, SdkException<DeletePaymentTokenError> ex)
    {
        if (ex.Error.TryGetError1(out Error1 e)) return Client1(action, e, 422);
        if (ex.Error.TryGetRawError(out RawError raw)) return TranslateRaw(action, raw);
        return Unknown(action);
    }

    private PaymentGatewayException Client(string action, Error e, int status)
    {
        _logger.LogWarning("PayPal rejected request to {Action}: {Name} (debugId {DebugId}).", action, e.Name, e.DebugId);
        var issue = e.Details?.FirstOrDefault()?.Issue;
        var detail = issue ?? e.Name ?? "the request was rejected";
        return new PaymentGatewayException($"PayPal could not {action}: {detail}.", status, e.DebugId);
    }

    private PaymentGatewayException Client1(string action, Error1 e, int status)
    {
        _logger.LogWarning("PayPal rejected request to {Action}: {Name} (debugId {DebugId}).", action, e.Name, e.DebugId);
        var issue = e.Details?.FirstOrDefault()?.Issue;
        var detail = issue ?? e.Name ?? "the request was rejected";
        return new PaymentGatewayException($"PayPal could not {action}: {detail}.", status, e.DebugId);
    }

    private PaymentGatewayException TranslateRaw(string action, RawError raw)
    {
        var status = (int)raw.StatusCode;
        _logger.LogWarning("PayPal returned HTTP {Status} while trying to {Action}.", status, action);
        return new PaymentGatewayException($"PayPal could not {action} (HTTP {status}).", status);
    }

    private PaymentGatewayException Server(string action, RawError raw)
    {
        var status = (int)raw.StatusCode;
        _logger.LogWarning("PayPal returned HTTP {Status} while trying to {Action}.", status, action);
        return new PaymentGatewayException($"PayPal is currently unable to {action}. Please try again later.", status);
    }

    private PaymentGatewayException Transport(string action, Exception ex)
    {
        _logger.LogWarning(ex, "PayPal was unreachable while trying to {Action}.", action);
        return new PaymentGatewayException($"PayPal is currently unreachable, so it could not {action}. Please try again later.", null, null, ex);
    }

    private PaymentGatewayException Parse(string action, JsonException ex)
    {
        _logger.LogWarning(ex, "PayPal returned an unreadable response while trying to {Action}.", action);
        return new PaymentGatewayException($"PayPal returned a response that could not be processed while trying to {action}.", null, null, ex);
    }

    private static PaymentGatewayException Unknown(string action) =>
        new($"PayPal could not {action}.");
}
