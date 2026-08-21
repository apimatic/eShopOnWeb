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

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// The one place the PayPal SDK is called. Translates the domain-facing <see cref="IPayPalGateway"/>
/// to/from the SDK, and converts every SDK failure into a <see cref="PaymentException"/> carrying an
/// operator-actionable message and an accurate HTTP status. No SDK type escapes this class.
/// </summary>
public class PayPalGateway : IPayPalGateway
{
    private readonly PayPalServerSdkClient _client;

    public PayPalGateway(PayPalServerSdkClient client, IOptions<PayPalSettings> settings)
    {
        _client = client;
        Currency = settings.Value.Currency ?? "USD";
    }

    public string Currency { get; }

    public async Task<AuthorizationResult> AuthorizeAsync(decimal amount, PaymentInstrument instrument, string idempotencyKey, CancellationToken ct = default)
    {
        var order = await CreateAuthorizeOrderAsync(amount, instrument, idempotencyKey, ct);

        var (authId, authStatus) = ExtractAuthorization(order.PurchaseUnits);
        if (authId is null)
        {
            GuardNoBrowserChallenge(order.Status?.Value, "authorize the order");

            var authResponse = await AuthorizeOrderAsync(order.Id!, idempotencyKey, ct);
            (authId, authStatus) = ExtractAuthorization(authResponse.PurchaseUnits);
            if (authId is null)
            {
                GuardNoBrowserChallenge(authResponse.Status?.Value, "authorize the order");
                throw new PaymentException("PayPal did not return an authorization for the order.", PaymentErrorKind.Gateway);
            }
        }

        return new AuthorizationResult(order.Id!, authId, authStatus ?? AuthorizationStatus.Created.Value, null, null, null);
    }

    private async Task<Order> CreateAuthorizeOrderAsync(decimal amount, PaymentInstrument instrument, string idempotencyKey, CancellationToken ct)
    {
        var card = instrument.Card is not null
            ? BuildCardRequest(instrument.Card)
            : new CardRequest { VaultId = instrument.VaultId };

        var body = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown { CurrencyCode = Currency, Value = FormatAmount(amount) }
                }
            },
            PaymentSource = new PaymentSource { Card = card }
        };

        var scope = BeginCall();
        try
        {
            return await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: ct);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            string? name = null, message = null, issue = null; int? st = null;
            if (ex.Error.TryGetError(out var e)) { name = e.Name; message = e.Message; issue = FirstIssue(e.Details); }
            else if (ex.Error.TryGetRawError(out var raw)) { st = (int)raw.StatusCode; message = SafeBody(raw); }
            throw MapTyped("create order", scope, ex, (name, message, issue, st));
        }
        catch (JsonException ex) { throw MapJson("create order", scope, ex); }
        catch (Exception ex) when (IsTransport(ex)) { throw MapTransport("create order", ex); }
    }

    private async Task<OrderAuthorizeResponse> AuthorizeOrderAsync(string orderId, string idempotencyKey, CancellationToken ct)
    {
        var scope = BeginCall();
        try
        {
            return await _client.Orders.AuthorizeOrder(
                id: orderId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey + "-auth",
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: null,
                prefer: "return=representation",
                ct: ct);
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            string? name = null, message = null, issue = null; int? st = null;
            if (ex.Error.TryGetError(out var e)) { name = e.Name; message = e.Message; issue = FirstIssue(e.Details); }
            else if (ex.Error.TryGetRawError(out var raw)) { st = (int)raw.StatusCode; message = SafeBody(raw); }
            throw MapTyped("authorize order", scope, ex, (name, message, issue, st));
        }
        catch (JsonException ex) { throw MapJson("authorize order", scope, ex); }
        catch (Exception ex) when (IsTransport(ex)) { throw MapTransport("authorize order", ex); }
    }

    public async Task<CaptureResult> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default)
    {
        var scope = BeginCall();
        try
        {
            var captured = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: null,
                prefer: "return=representation",
                ct: ct);

            var breakdown = captured.SellerReceivableBreakdown;
            // The captured amount is the transaction amount PayPal actually took; the fee and net come
            // from the seller-receivable breakdown as PayPal reports them.
            var capturedAmount = ParseAmountOrNull(captured.Amount?.Value)
                ?? ParseAmountOrNull(breakdown?.GrossAmount?.Value)
                ?? 0m;

            return new CaptureResult(
                captured.Id!,
                captured.Status?.Value ?? CaptureStatus.Completed.Value,
                capturedAmount,
                ParseAmountOrNull(breakdown?.PaypalFee?.Value),
                ParseAmountOrNull(breakdown?.NetAmount?.Value),
                captured.Amount?.CurrencyCode ?? Currency);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            string? name = null, message = null, issue = null; int? st = null;
            if (ex.Error.TryGetError(out var e)) { name = e.Name; message = e.Message; issue = FirstIssue(e.Details); }
            else if (ex.Error.TryGetNoContent(out var nc)) { st = (int)nc.StatusCode; message = SafeBody(nc); }
            else if (ex.Error.TryGetRawError(out var raw)) { st = (int)raw.StatusCode; message = SafeBody(raw); }
            throw MapTyped("capture payment", scope, ex, (name, message, issue, st));
        }
        catch (JsonException ex) { throw MapJson("capture payment", scope, ex); }
        catch (Exception ex) when (IsTransport(ex)) { throw MapTransport("capture payment", ex); }
    }

    public async Task<ReauthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, CancellationToken ct = default)
    {
        var scope = BeginCall();
        try
        {
            var auth = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: null,
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest { Amount = new Money { CurrencyCode = Currency, Value = FormatAmount(amount) } },
                prefer: "return=representation",
                ct: ct);

            return new ReauthorizationResult(auth.Id!, auth.Status?.Value ?? AuthorizationStatus.Created.Value, auth.ExpirationTime);
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            string? name = null, message = null, issue = null; int? st = null;
            if (ex.Error.TryGetError(out var e)) { name = e.Name; message = e.Message; issue = FirstIssue(e.Details); }
            else if (ex.Error.TryGetNoContent(out var nc)) { st = (int)nc.StatusCode; message = SafeBody(nc); }
            else if (ex.Error.TryGetRawError(out var raw)) { st = (int)raw.StatusCode; message = SafeBody(raw); }
            throw MapTyped("re-authorize payment", scope, ex, (name, message, issue, st));
        }
        catch (JsonException ex) { throw MapJson("re-authorize payment", scope, ex); }
        catch (Exception ex) when (IsTransport(ex)) { throw MapTransport("re-authorize payment", ex); }
    }

    public async Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default)
    {
        var scope = BeginCall();
        try
        {
            await _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: idempotencyKey,
                prefer: "return=minimal",
                ct: ct);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            string? name = null, message = null, issue = null; int? st = null;
            if (ex.Error.TryGetError(out var e)) { name = e.Name; message = e.Message; issue = FirstIssue(e.Details); }
            else if (ex.Error.TryGetNoContent(out var nc)) { st = (int)nc.StatusCode; message = SafeBody(nc); }
            else if (ex.Error.TryGetRawError(out var raw)) { st = (int)raw.StatusCode; message = SafeBody(raw); }
            throw MapTyped("void authorization", scope, ex, (name, message, issue, st));
        }
        // A successful void returns 204 No Content; the SDK cannot deserialize the empty body and throws
        // JsonException. An empty body on a 2xx here IS the success signal.
        catch (JsonException) when (scope.StatusCode is >= 200 and < 300) { }
        catch (JsonException ex) { throw MapJson("void authorization", scope, ex); }
        catch (Exception ex) when (IsTransport(ex)) { throw MapTransport("void authorization", ex); }
    }

    public async Task<RefundResult> RefundAsync(string captureId, decimal amount, string idempotencyKey, CancellationToken ct = default)
    {
        var scope = BeginCall();
        try
        {
            var refund = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: new RefundRequest { Amount = new Money { CurrencyCode = Currency, Value = FormatAmount(amount) } },
                prefer: "return=representation",
                ct: ct);

            return new RefundResult(refund.Id!, refund.Status?.Value ?? RefundStatus.Completed.Value, ParseAmountOrNull(refund.Amount?.Value) ?? amount);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            string? name = null, message = null, issue = null; int? st = null;
            if (ex.Error.TryGetError(out var e)) { name = e.Name; message = e.Message; issue = FirstIssue(e.Details); }
            else if (ex.Error.TryGetNoContent(out var nc)) { st = (int)nc.StatusCode; message = SafeBody(nc); }
            else if (ex.Error.TryGetRawError(out var raw)) { st = (int)raw.StatusCode; message = SafeBody(raw); }
            throw MapTyped("refund payment", scope, ex, (name, message, issue, st));
        }
        catch (JsonException ex) { throw MapJson("refund payment", scope, ex); }
        catch (Exception ex) when (IsTransport(ex)) { throw MapTransport("refund payment", ex); }
    }

    public async Task<VaultCardResult> VaultCardAsync(CardDetails card, string idempotencyKey, CancellationToken ct = default)
    {
        var scope = BeginCall();
        try
        {
            var token = await _client.Vault.CreatePaymentToken(
                payPalRequestId: idempotencyKey,
                body: new PaymentTokenRequest
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
                },
                ct: ct);

            var entity = token.PaymentSource?.Card;
            return new VaultCardResult(token.Id!, entity?.Brand?.Value, entity?.LastDigits, entity?.Expiry, null);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            string? name = null, message = null, issue = null; int? st = null;
            if (ex.Error.TryGetError1(out var e)) { name = e.Name; message = e.Message; issue = FirstIssue(e.Details); }
            else if (ex.Error.TryGetRawError(out var raw)) { st = (int)raw.StatusCode; message = SafeBody(raw); }
            throw MapTyped("vault card", scope, ex, (name, message, issue, st));
        }
        catch (JsonException ex) { throw MapJson("vault card", scope, ex); }
        catch (Exception ex) when (IsTransport(ex)) { throw MapTransport("vault card", ex); }
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default)
    {
        var scope = BeginCall();
        try
        {
            await _client.Vault.DeletePaymentToken(id: vaultId, ct: ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            string? name = null, message = null, issue = null; int? st = null;
            if (ex.Error.TryGetError1(out var e)) { name = e.Name; message = e.Message; issue = FirstIssue(e.Details); }
            else if (ex.Error.TryGetRawError(out var raw)) { st = (int)raw.StatusCode; message = SafeBody(raw); }
            throw MapTyped("delete vaulted card", scope, ex, (name, message, issue, st));
        }
        catch (JsonException ex) { throw MapJson("delete vaulted card", scope, ex); }
        catch (Exception ex) when (IsTransport(ex)) { throw MapTransport("delete vaulted card", ex); }
    }

    public async Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var results = new List<PayPalTransaction>();
        var page = 1;
        int totalPages;

        do
        {
            var scope = BeginCall();
            SearchResponse response;
            try
            {
                response = await _client.TransactionSearch.SearchTransactions(
                    startDate: FormatDate(from),
                    endDate: FormatDate(to),
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
            catch (SdkException<RawError> ex)
            {
                throw MapTyped("transaction search", scope, ex, (null, SafeBody(ex.Error), null, (int)ex.Error.StatusCode));
            }
            catch (JsonException ex) { throw MapJson("transaction search", scope, ex); }
            catch (Exception ex) when (IsTransport(ex)) { throw MapTransport("transaction search", ex); }

            totalPages = response.TotalPages ?? 1;

            if (response.TransactionDetails is not null)
            {
                foreach (var detail in response.TransactionDetails)
                {
                    var info = detail.TransactionInfo;
                    if (info is null) continue;

                    results.Add(new PayPalTransaction(
                        info.TransactionId,
                        info.TransactionStatus,
                        ParseAmountOrNull(info.TransactionAmount?.Value),
                        info.TransactionAmount?.CurrencyCode,
                        ParseDate(info.TransactionInitiationDate),
                        info.TransactionEventCode));
                }
            }

            page++;
        }
        while (page <= totalPages);

        return results;
    }

    // ---- helpers ----

    private static PayPalCallScope BeginCall()
    {
        var scope = new PayPalCallScope();
        PayPalCallContext.Current.Value = scope;
        return scope;
    }

    private static void GuardNoBrowserChallenge(string? status, string action)
    {
        if (string.Equals(status, OrderStatus.PayerActionRequired.Value, StringComparison.Ordinal))
            throw new PaymentException(
                $"PayPal requires the shopper to approve this card in a browser (payer action required) to {action}. " +
                "This API supports only direct card payments and does not perform a browser approval round-trip.",
                PaymentErrorKind.ChallengeRequired);
    }

    private static (string? Id, string? Status) ExtractAuthorization(IReadOnlyList<PurchaseUnit>? purchaseUnits)
    {
        if (purchaseUnits is null) return (null, null);
        foreach (var unit in purchaseUnits)
        {
            var authorizations = unit.Payments?.Authorizations;
            if (authorizations is null) continue;
            foreach (var authorization in authorizations)
            {
                if (!string.IsNullOrEmpty(authorization.Id))
                    return (authorization.Id, authorization.Status?.Value);
            }
        }
        return (null, null);
    }

    private static CardRequest BuildCardRequest(CardDetails card) => new CardRequest
    {
        Number = card.Number,
        Expiry = card.Expiry,
        SecurityCode = card.SecurityCode,
        Name = card.CardholderName,
        BillingAddress = BuildAddress(card.BillingAddress)
    };

    private static Address? BuildAddress(CardBillingAddress? address)
    {
        if (address is null) return null;
        return new Address
        {
            CountryCode = string.IsNullOrWhiteSpace(address.CountryCode) ? "US" : address.CountryCode!,
            AddressLine1 = address.Line1,
            AddressLine2 = address.Line2,
            AdminArea1 = address.State,
            AdminArea2 = address.City,
            PostalCode = address.PostalCode
        };
    }

    private static string? FirstIssue(IReadOnlyList<ErrorDetails>? details) =>
        details is { Count: > 0 } ? details[0].Issue : null;

    private static string? FirstIssue(IReadOnlyList<ErrorDetails1>? details) =>
        details is { Count: > 0 } ? details[0].Issue : null;

    private static string FormatAmount(decimal amount) =>
        Math.Round(amount, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal ParseAmount(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0m;

    private static decimal? ParseAmountOrNull(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d) ? d : null;

    private static bool IsTransport(Exception ex) => ex is HttpRequestException or TaskCanceledException or OperationCanceledException;

    private static string SafeBody(RawError raw)
    {
        try
        {
            var body = raw.ReadAsString();
            return string.IsNullOrWhiteSpace(body) ? $"HTTP {(int)raw.StatusCode}" : Truncate(body, 500);
        }
        catch
        {
            return $"HTTP {(int)raw.StatusCode}";
        }
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value.Substring(0, max);

    private static PaymentException MapTyped(string operation, PayPalCallScope scope, Exception inner, (string? Name, string? Message, string? Issue, int? Status) parts)
    {
        var status = parts.Status ?? scope.StatusCode;
        var kind = KindForStatus(status);
        var detail = ComposeDetail(parts.Name, parts.Message, parts.Issue, status);
        return new PaymentException($"PayPal {operation} failed: {detail}", kind, inner);
    }

    private static PaymentException MapJson(string operation, PayPalCallScope scope, JsonException inner)
    {
        // A 2xx body that no longer matches the model is a genuine unknown (5xx). A non-2xx body that
        // could not be parsed into the typed error model is still a rejection (4xx) — the handler captured
        // the status before the SDK discarded it, so map on that, not blanket 5xx.
        var status = scope.StatusCode;
        var kind = status is >= 400 and < 500 ? PaymentErrorKind.BusinessRule : PaymentErrorKind.Gateway;
        return new PaymentException($"PayPal {operation} returned a response that could not be processed.", kind, inner);
    }

    private static PaymentException MapTransport(string operation, Exception inner) =>
        new PaymentException($"PayPal {operation} could not be completed because PayPal was unreachable.", PaymentErrorKind.Gateway, inner);

    private static PaymentErrorKind KindForStatus(int? status) => status switch
    {
        400 => PaymentErrorKind.Validation,
        401 => PaymentErrorKind.Gateway,
        403 => PaymentErrorKind.Forbidden,
        404 => PaymentErrorKind.NotFound,
        409 => PaymentErrorKind.Conflict,
        422 => PaymentErrorKind.BusinessRule,
        >= 500 => PaymentErrorKind.Gateway,
        null => PaymentErrorKind.Gateway,
        _ => PaymentErrorKind.Validation
    };

    private static string ComposeDetail(string? name, string? message, string? issue, int? status)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(message)) parts.Add(message!);
        if (!string.IsNullOrWhiteSpace(issue) && !string.Equals(issue, name, StringComparison.OrdinalIgnoreCase)) parts.Add($"[{issue}]");
        else if (parts.Count == 0 && !string.IsNullOrWhiteSpace(name)) parts.Add(name!);
        if (parts.Count == 0) parts.Add(status is not null ? $"HTTP {status}" : "unknown error");
        return string.Join(" ", parts);
    }
}
