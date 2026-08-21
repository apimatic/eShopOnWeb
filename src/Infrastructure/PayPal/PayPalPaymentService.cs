using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// The single seam between eShop and PayPal. Each method wraps one PayPal operation, translating SDK
/// success/failure into plain domain types and <see cref="PayPalProviderException"/>s. Full card numbers
/// flow straight through to PayPal and are never persisted or logged here.
/// </summary>
public class PayPalPaymentService : IPayPalPaymentService
{
    private readonly PayPalServerSdkClient _client;
    private readonly string _currency;

    public PayPalPaymentService(PayPalServerSdkClient client, IOptions<PayPalSettings> settings)
    {
        _client = client;
        _currency = settings.Value.Currency;
    }

    public string Currency => _currency;

    // --- Authorize ----------------------------------------------------------

    public Task<AuthorizationResult> AuthorizeWithCardAsync(decimal amount, string currency,
        CardPaymentDetails card, string idempotencyKey, CancellationToken ct = default)
    {
        var cardRequest = new CardRequest
        {
            Number = card.Number,
            Expiry = card.ExpiryYearMonth,
            SecurityCode = card.SecurityCode,
            Name = card.CardholderName
        };
        return AuthorizeCoreAsync(amount, currency, cardRequest, idempotencyKey, ct);
    }

    public Task<AuthorizationResult> AuthorizeWithVaultedCardAsync(decimal amount, string currency,
        string vaultId, string? payPalCustomerId, string idempotencyKey, CancellationToken ct = default)
    {
        // Replay the owning customer id so PayPal permits charging the vaulted card
        // (payment_source.card.attributes.customer.id).
        var cardRequest = new CardRequest
        {
            VaultId = vaultId,
            Attributes = string.IsNullOrEmpty(payPalCustomerId)
                ? null
                : new CardAttributes { Customer = new CardCustomerInformation { Id = payPalCustomerId } }
        };

        return AuthorizeCoreAsync(amount, currency, cardRequest, idempotencyKey, ct);
    }

    private async Task<AuthorizationResult> AuthorizeCoreAsync(decimal amount, string currency,
        CardRequest card, string idempotencyKey, CancellationToken ct)
    {
        // Create the order shell with intent=AUTHORIZE (no payment source yet), then supply the card on the
        // authorize call. Putting the card on CreateOrder makes PayPal authorize inline, which then rejects
        // the explicit AuthorizeOrder as ORDER_ALREADY_AUTHORIZED — so the card belongs on AuthorizeOrder.
        var orderRequest = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown { CurrencyCode = currency, Value = FormatAmount(amount) }
                }
            }
        };

        Order createdOrder;
        try
        {
            createdOrder = await _client.Orders.CreateOrder(
                payPalMockResponse: null, payPalRequestId: $"{idempotencyKey}-create",
                payPalPartnerAttributionId: null, payPalClientMetadataId: null, payPalAuthAssertion: null,
                body: orderRequest, prefer: "return=representation", requestOptions: null, ct: ct);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            if (ex.Error.TryGetError(out Error? err) && err is not null) throw FromError(err, "create the PayPal order");
            if (ex.Error.TryGetRawError(out RawError? raw) && raw is not null) throw FromRaw(raw, "create the PayPal order");
            throw ProviderFailure("create the PayPal order", ex);
        }
        catch (JsonException ex) { throw Unprocessable(ex); }
        catch (Exception ex) when (IsTransport(ex)) { throw Unreachable(ex); }

        if (createdOrder.Status == OrderStatus.PayerActionRequired) throw PayerAction();

        var authorizeRequest = new OrderAuthorizeRequest
        {
            PaymentSource = new OrderAuthorizeRequestPaymentSource { Card = card }
        };

        OrderAuthorizeResponse authResponse;
        try
        {
            authResponse = await _client.Orders.AuthorizeOrder(
                id: createdOrder.Id, payPalMockResponse: null, payPalRequestId: $"{idempotencyKey}-authorize",
                payPalClientMetadataId: null, payPalAuthAssertion: null, body: authorizeRequest,
                prefer: "return=representation", requestOptions: null, ct: ct);
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            if (ex.Error.TryGetError(out Error? err) && err is not null) throw FromError(err, "authorize the PayPal order");
            if (ex.Error.TryGetRawError(out RawError? raw) && raw is not null) throw FromRaw(raw, "authorize the PayPal order");
            throw ProviderFailure("authorize the PayPal order", ex);
        }
        catch (JsonException ex) { throw Unprocessable(ex); }
        catch (Exception ex) when (IsTransport(ex)) { throw Unreachable(ex); }

        if (authResponse.Status == OrderStatus.PayerActionRequired) throw PayerAction();

        var authorization = authResponse.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
        if (authorization is null || string.IsNullOrEmpty(authorization.Id))
        {
            throw new PayPalProviderException(
                "PayPal did not return an authorization for the order; the card may have been declined.", 422);
        }

        var payPalOrderId = createdOrder.Id ?? authResponse.Id
            ?? throw new PayPalProviderException("PayPal did not return an order id.", 502);

        return new AuthorizationResult(payPalOrderId, authorization.Id!,
            authorization.Status?.Value, ParseDate(authorization.ExpirationTime));
    }

    // --- Capture ------------------------------------------------------------

    public async Task<CaptureResult> CaptureAsync(string authorizationId, string idempotencyKey,
        CancellationToken ct = default)
    {
        CapturedPayment captured;
        try
        {
            captured = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId, payPalMockResponse: null, payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null, body: null, prefer: "return=representation", requestOptions: null, ct: ct);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out Error? err) && err is not null) throw FromError(err, "capture the payment");
            if (ex.Error.TryGetNoContent(out RawError? nc) && nc is not null) throw FromRaw(nc, "capture the payment");
            if (ex.Error.TryGetRawError(out RawError? raw) && raw is not null) throw FromRaw(raw, "capture the payment");
            throw ProviderFailure("capture the payment", ex);
        }
        catch (JsonException ex) { throw Unprocessable(ex); }
        catch (Exception ex) when (IsTransport(ex)) { throw Unreachable(ex); }

        var breakdown = captured.SellerReceivableBreakdown;
        var gross = breakdown?.GrossAmount is { } g ? ParseMoney(g)
            : captured.Amount is { } a ? ParseMoney(a) : 0m;
        decimal? fee = breakdown?.PaypalFee is { } f ? ParseMoney(f) : null;
        decimal? net = breakdown?.NetAmount is { } n ? ParseMoney(n) : null;

        return new CaptureResult(
            captured.Id ?? throw MissingId("capture"),
            captured.Status?.Value, gross, fee, net);
    }

    // --- Reauthorize --------------------------------------------------------

    public async Task<ReauthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, CancellationToken ct = default)
    {
        var body = new ReauthorizeRequest
        {
            Amount = new Money { CurrencyCode = currency, Value = FormatAmount(amount) }
        };

        PaymentAuthorization reauth;
        try
        {
            reauth = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId, payPalRequestId: null, payPalAuthAssertion: null,
                body: body, prefer: "return=representation", requestOptions: null, ct: ct);
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            if (ex.Error.TryGetError(out Error? err) && err is not null) throw FromError(err, "renew the authorization");
            if (ex.Error.TryGetNoContent(out RawError? nc) && nc is not null) throw FromRaw(nc, "renew the authorization");
            if (ex.Error.TryGetRawError(out RawError? raw) && raw is not null) throw FromRaw(raw, "renew the authorization");
            throw ProviderFailure("renew the authorization", ex);
        }
        catch (JsonException ex) { throw Unprocessable(ex); }
        catch (Exception ex) when (IsTransport(ex)) { throw Unreachable(ex); }

        return new ReauthorizationResult(
            reauth.Id ?? throw MissingId("authorization"),
            reauth.Status?.Value, ParseDate(reauth.ExpirationTime));
    }

    // --- Void ---------------------------------------------------------------

    public async Task VoidAsync(string authorizationId, CancellationToken ct = default)
    {
        try
        {
            // return=representation so PayPal returns the voided authorization body; return=minimal yields
            // a 204 with no body, which the SDK cannot deserialize into PaymentAuthorization.
            await _client.Payments.VoidPayment(
                authorizationId: authorizationId, payPalMockResponse: null, payPalAuthAssertion: null,
                payPalRequestId: null, prefer: "return=representation", requestOptions: null, ct: ct);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            if (ex.Error.TryGetError(out Error? err) && err is not null) throw FromError(err, "release the authorization");
            if (ex.Error.TryGetNoContent(out RawError? nc) && nc is not null) throw FromRaw(nc, "release the authorization");
            if (ex.Error.TryGetRawError(out RawError? raw) && raw is not null) throw FromRaw(raw, "release the authorization");
            throw ProviderFailure("release the authorization", ex);
        }
        catch (JsonException ex) { throw Unprocessable(ex); }
        catch (Exception ex) when (IsTransport(ex)) { throw Unreachable(ex); }
    }

    // --- Refund -------------------------------------------------------------

    public async Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, CancellationToken ct = default)
    {
        RefundRequest? body = amount.HasValue
            ? new RefundRequest { Amount = new Money { CurrencyCode = currency, Value = FormatAmount(amount.Value) } }
            : null;

        Refund refund;
        try
        {
            refund = await _client.Payments.RefundCapturedPayment(
                captureId: captureId, payPalMockResponse: null, payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null, body: body, prefer: "return=representation", requestOptions: null, ct: ct);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out Error? err) && err is not null) throw FromError(err, "refund the payment");
            if (ex.Error.TryGetNoContent(out RawError? nc) && nc is not null) throw FromRaw(nc, "refund the payment");
            if (ex.Error.TryGetRawError(out RawError? raw) && raw is not null) throw FromRaw(raw, "refund the payment");
            throw ProviderFailure("refund the payment", ex);
        }
        catch (JsonException ex) { throw Unprocessable(ex); }
        catch (Exception ex) when (IsTransport(ex)) { throw Unreachable(ex); }

        decimal? refundedAmount = refund.Amount is { } a ? ParseMoney(a) : null;
        decimal? totalRefunded = refund.SellerPayableBreakdown?.TotalRefundedAmount is { } t ? ParseMoney(t) : null;

        return new RefundResult(refund.Id ?? throw MissingId("refund"),
            refund.Status?.Value, refundedAmount, totalRefunded);
    }

    // --- Vault --------------------------------------------------------------

    public async Task<VaultedCardResult> VaultCardAsync(CardPaymentDetails card, string merchantCustomerId,
        string idempotencyKey, CancellationToken ct = default)
    {
        var body = new PaymentTokenRequest
        {
            // Vault under a stable customer so the card can be reused to pay later.
            Customer = new Customer { MerchantCustomerId = merchantCustomerId },
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Number = card.Number,
                    Expiry = card.ExpiryYearMonth,
                    SecurityCode = card.SecurityCode,
                    Name = card.CardholderName
                }
            }
        };

        PaymentTokenResponse token;
        try
        {
            token = await _client.Vault.CreatePaymentToken(
                payPalRequestId: idempotencyKey, body: body, requestOptions: null, ct: ct);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError1(out Error1? err) && err is not null) throw FromError1(err, "save the card");
            if (ex.Error.TryGetRawError(out RawError? raw) && raw is not null) throw FromRaw(raw, "save the card");
            throw ProviderFailure("save the card", ex);
        }
        catch (JsonException ex) { throw Unprocessable(ex); }
        catch (Exception ex) when (IsTransport(ex)) { throw Unreachable(ex); }

        var cardEntity = token.PaymentSource?.Card;
        return new VaultedCardResult(
            token.Id ?? throw MissingId("vault"),
            token.Customer?.Id,
            cardEntity?.Brand?.Value, cardEntity?.LastDigits, cardEntity?.Expiry, card.CardholderName);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(id: vaultId, requestOptions: null, ct: ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError1(out Error1? err) && err is not null) throw FromError1(err, "remove the card");
            if (ex.Error.TryGetRawError(out RawError? raw) && raw is not null) throw FromRaw(raw, "remove the card");
            throw ProviderFailure("remove the card", ex);
        }
        catch (JsonException ex) { throw Unprocessable(ex); }
        catch (Exception ex) when (IsTransport(ex)) { throw Unreachable(ex); }
    }

    // --- Reconciliation (transaction search, paged over the whole range) -----

    public async Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(string startDate,
        string endDate, CancellationToken ct = default)
    {
        var results = new List<PayPalTransactionRecord>();
        var page = 1;
        int totalPages;

        do
        {
            SearchResponse response;
            try
            {
                response = await _client.TransactionSearch.SearchTransactions(
                    startDate: startDate, endDate: endDate, transactionId: null, transactionType: null,
                    transactionStatus: null, transactionAmount: null, transactionCurrency: null,
                    paymentInstrumentType: null, storeId: null, terminalId: null, fields: "transaction_info",
                    balanceAffectingRecordsOnly: "Y", pageSize: 100, page: page, requestOptions: null, ct: ct);
            }
            catch (SdkException<RawError> ex) { throw FromRaw(ex.Error, "read the transaction report"); }
            catch (JsonException ex) { throw Unprocessable(ex); }
            catch (Exception ex) when (IsTransport(ex)) { throw Unreachable(ex); }

            if (response.TransactionDetails is not null)
            {
                foreach (var detail in response.TransactionDetails)
                {
                    var info = detail.TransactionInfo;
                    if (info?.TransactionId is null) continue;

                    decimal? amount = info.TransactionAmount is { } m ? ParseMoney(m) : null;
                    results.Add(new PayPalTransactionRecord(
                        info.TransactionId, amount, info.TransactionAmount?.CurrencyCode,
                        info.TransactionStatus, ParseDate(info.TransactionInitiationDate)));
                }
            }

            totalPages = response.TotalPages ?? 0;
            page++;
        }
        while (page <= totalPages);

        return results;
    }

    // --- Helpers ------------------------------------------------------------

    private static string FormatAmount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal ParseMoney(Money money) => decimal.Parse(money.Value, CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
            ? dt : null;

    private static bool IsTransport(Exception ex) => ex is HttpRequestException or TaskCanceledException;

    private static PayPalProviderException Unprocessable(Exception ex) =>
        new("PayPal returned a response that could not be processed.", 502, ex);

    private static PayPalProviderException Unreachable(Exception ex) =>
        new("PayPal could not be reached. Please try again.", 502, ex);

    private static PayPalProviderException ProviderFailure(string what, Exception ex) =>
        new($"PayPal could not {what}.", 502, ex);

    private static PayerActionRequiredException PayerAction() => new(
        "PayPal answered with a challenge that requires the shopper to approve the payment in a browser " +
        "(PAYER_ACTION_REQUIRED). This integration is browser-free by design — STOP and report this to the shopper.");

    private static PayPalProviderException MissingId(string what) =>
        new($"PayPal did not return a {what} id.", 502);

    private static PayPalProviderException FromError(Error error, string what)
    {
        var message = BuildMessage(error.Message, error.Name,
            error.Details?.Select(d => (d.Issue, d.Description)));
        return new PayPalProviderException($"PayPal could not {what}: {message}", 422);
    }

    private static PayPalProviderException FromError1(Error1 error, string what)
    {
        var message = BuildMessage(error.Message, error.Name,
            error.Details?.Select(d => (d.Issue, d.Description)));
        return new PayPalProviderException($"PayPal could not {what}: {message}", 422);
    }

    private static PayPalProviderException FromRaw(RawError raw, string what)
    {
        var status = (int)raw.StatusCode;
        string body;
        try { body = raw.ReadAsString() ?? string.Empty; }
        catch { body = string.Empty; }
        if (body.Length > 500) body = body[..500];

        var surfaced = status is >= 400 and < 500 ? status : 502;
        var suffix = string.IsNullOrWhiteSpace(body) ? string.Empty : $": {body}";
        return new PayPalProviderException($"PayPal could not {what} (HTTP {status}){suffix}", surfaced);
    }

    private static string BuildMessage(string message, string name,
        IEnumerable<(string Issue, string? Description)>? details)
    {
        var text = string.IsNullOrWhiteSpace(message) ? name : message;
        if (details is not null)
        {
            var issues = string.Join("; ", details
                .Where(d => !string.IsNullOrEmpty(d.Issue))
                .Select(d => string.IsNullOrEmpty(d.Description) ? d.Issue : $"{d.Issue} — {d.Description}"));
            if (issues.Length > 0) text += $" [{issues}]";
        }
        return text;
    }
}
