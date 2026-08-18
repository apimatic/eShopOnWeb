using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// The only class that talks to PayPal. It translates the app's own payment contracts to and from
/// the PayPal .NET SDK, and every SDK failure into an ApplicationCore payment exception, so no SDK
/// type or raw provider detail ever leaks past this boundary. Card data flows through as arguments
/// only — it is never persisted here and never logged.
/// </summary>
public class PayPalPaymentService : IPayPalPaymentService
{
    private readonly PayPalServerSdkClient _client;
    private readonly ILogger<PayPalPaymentService> _logger;

    public PayPalPaymentService(PayPalServerSdkClient client, ILogger<PayPalPaymentService> logger)
    {
        _client = client;
        _logger = logger;
    }

    // ---------------------------------------------------------------------------------------------
    // Authorize (hold): CreateOrder(intent=AUTHORIZE) with the card inline, then AuthorizeOrder if
    // the authorization was not already created inline.
    // ---------------------------------------------------------------------------------------------
    public async Task<PayPalAuthorizationResult> AuthorizeAsync(decimal amount, string currencyCode, PayPalPaymentSource source, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var orderRequest = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = currencyCode,
                        Value = FormatAmount(amount, currencyCode)
                    }
                }
            },
            PaymentSource = new PaymentSource { Card = BuildCardRequest(source) }
        };

        var order = await CreateOrderAsync(orderRequest, idempotencyKey, cancellationToken);

        var createStatus = order.Status?.Value;
        if (RequiresApproval(createStatus, order.Links))
            throw ChallengeRequired();

        var payPalOrderId = order.Id ?? throw Gateway("create order", "PayPal did not return an order id.", 502, null);
        var auth = ExtractAuthorization(order.PurchaseUnits);

        if (auth.AuthorizationId is null)
        {
            // No inline authorization — request one explicitly. The card was already supplied on the order.
            var authResponse = await AuthorizeOrderAsync(payPalOrderId, idempotencyKey + ":authorize", cancellationToken);

            if (RequiresApproval(authResponse.Status?.Value, authResponse.Links))
                throw ChallengeRequired();

            if (authResponse.Id is not null)
                payPalOrderId = authResponse.Id;

            auth = ExtractAuthorization(authResponse.PurchaseUnits);
        }

        if (auth.AuthorizationId is null)
            throw ChallengeRequired();

        return new PayPalAuthorizationResult(payPalOrderId, auth.AuthorizationId, auth.Status, auth.ExpiresAt);
    }

    private async Task<Order> CreateOrderAsync(OrderRequest body, string idempotencyKey, CancellationToken ct)
    {
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
            ex.Error.TryGetError(out var typed);
            ex.Error.TryGetRawError(out var raw);
            throw GatewayFromTyped("create order", typed?.Message, raw, typed is not null, ex);
        }
        catch (Exception ex) when (IsTransport(ex)) { throw Transport("create order", ex); }
        catch (JsonException ex) { throw BrokenBody("create order", ex); }
    }

    private async Task<OrderAuthorizeResponse> AuthorizeOrderAsync(string orderId, string idempotencyKey, CancellationToken ct)
    {
        try
        {
            return await _client.Orders.AuthorizeOrder(
                id: orderId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: null,
                prefer: "return=representation",
                ct: ct);
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            ex.Error.TryGetError(out var typed);
            ex.Error.TryGetRawError(out var raw);
            throw GatewayFromTyped("authorize order", typed?.Message, raw, typed is not null, ex);
        }
        catch (Exception ex) when (IsTransport(ex)) { throw Transport("authorize order", ex); }
        catch (JsonException ex) { throw BrokenBody("authorize order", ex); }
    }

    // ---------------------------------------------------------------------------------------------
    // Capture (take the money)
    // ---------------------------------------------------------------------------------------------
    public async Task<PayPalCaptureResult> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        CapturedPayment captured;
        try
        {
            captured = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: null,
                prefer: "return=representation",
                ct: cancellationToken);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            ex.Error.TryGetError(out var typed);
            ex.Error.TryGetRawError(out var raw);
            throw GatewayFromTyped("capture payment", typed?.Message, raw, typed is not null, ex);
        }
        catch (Exception ex) when (IsTransport(ex)) { throw Transport("capture payment", ex); }
        catch (JsonException ex) { throw BrokenBody("capture payment", ex); }

        var breakdown = captured.SellerReceivableBreakdown;
        var capturedAmount = ParseMoney(captured.Amount) ?? ParseMoney(breakdown?.GrossAmount) ?? 0m;
        var currency = captured.Amount?.CurrencyCode ?? breakdown?.GrossAmount?.CurrencyCode ?? string.Empty;

        return new PayPalCaptureResult(
            captured.Id ?? throw Gateway("capture payment", "PayPal did not return a capture id.", 502, null),
            captured.Status?.Value,
            capturedAmount,
            ParseMoney(breakdown?.PaypalFee),
            ParseMoney(breakdown?.NetAmount),
            currency);
    }

    // ---------------------------------------------------------------------------------------------
    // Re-authorize a stale hold
    // ---------------------------------------------------------------------------------------------
    public async Task<PayPalReauthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var reauth = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest
                {
                    Amount = new Money { CurrencyCode = currencyCode, Value = FormatAmount(amount, currencyCode) }
                },
                prefer: "return=representation",
                ct: cancellationToken);

            return new PayPalReauthorizationResult(
                reauth.Id ?? authorizationId,
                reauth.Status?.Value,
                ParseDate(reauth.ExpirationTime));
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            ex.Error.TryGetError(out var typed);
            // A rejected re-authorization is terminal: the hold can no longer be renewed and an operator
            // must create a fresh payment. Surface that in operator-actionable terms.
            _logger.LogWarning(ex, "PayPal re-authorization rejected for authorization {AuthorizationId}", authorizationId);
            throw new ReauthorizationExpiredException(
                $"The authorization can no longer be renewed and this order cannot be captured; an operator must arrange a new payment.{FormatDetail(typed?.Message)}",
                ex);
        }
        catch (Exception ex) when (IsTransport(ex)) { throw Transport("re-authorize payment", ex); }
        catch (JsonException ex) { throw BrokenBody("re-authorize payment", ex); }
    }

    // ---------------------------------------------------------------------------------------------
    // Void a hold (cancel before fulfilment)
    // ---------------------------------------------------------------------------------------------
    public async Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: idempotencyKey,
                prefer: "return=minimal",
                ct: cancellationToken);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            // A genuine void failure (e.g. already captured) is delivered as a typed error here.
            ex.Error.TryGetError(out var typed);
            ex.Error.TryGetRawError(out var raw);
            throw GatewayFromTyped("void authorization", typed?.Message, raw, typed is not null, ex);
        }
        catch (Exception ex) when (IsTransport(ex)) { throw Transport("void authorization", ex); }
        catch (JsonException)
        {
            // A successful void returns 204 No Content. The SDK still tries to deserialize the (empty)
            // body into a PaymentAuthorization and throws — an empty body IS the success signal for void.
            // A real rejection arrives above as SdkException<VoidPaymentError>, not here.
            _logger.LogInformation("PayPal void returned an empty body (204) for authorization {AuthorizationId}; treated as success.", authorizationId);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Refund a capture (full or partial)
    // ---------------------------------------------------------------------------------------------
    public async Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount, string currencyCode, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        RefundRequest? body = amount is decimal value
            ? new RefundRequest { Amount = new Money { CurrencyCode = currencyCode, Value = FormatAmount(value, currencyCode) } }
            : null; // null body => full refund of the remaining balance

        try
        {
            var refund = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: cancellationToken);

            return new PayPalRefundResult(
                refund.Id ?? throw Gateway("refund payment", "PayPal did not return a refund id.", 502, null),
                refund.Status?.Value,
                ParseMoney(refund.Amount) ?? amount ?? 0m);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            ex.Error.TryGetError(out var typed);
            ex.Error.TryGetRawError(out var raw);
            throw GatewayFromTyped("refund payment", typed?.Message, raw, typed is not null, ex);
        }
        catch (Exception ex) when (IsTransport(ex)) { throw Transport("refund payment", ex); }
        catch (JsonException ex) { throw BrokenBody("refund payment", ex); }
    }

    // ---------------------------------------------------------------------------------------------
    // Vault a card
    // ---------------------------------------------------------------------------------------------
    public async Task<PayPalVaultedCard> VaultCardAsync(CardDetails card, string idempotencyKey, CancellationToken cancellationToken = default)
    {
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

        try
        {
            var token = await _client.Vault.CreatePaymentToken(
                payPalRequestId: idempotencyKey,
                body: body,
                ct: cancellationToken);

            var entity = token.PaymentSource?.Card;
            return new PayPalVaultedCard(
                token.Id ?? throw Gateway("save card", "PayPal did not return a vault id.", 502, null),
                entity?.Brand?.Value,
                entity?.LastDigits,
                entity?.Expiry,
                entity?.Name);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            ex.Error.TryGetError1(out var typed);
            ex.Error.TryGetRawError(out var raw);
            throw GatewayFromTyped("save card", typed?.Message, raw, typed is not null, ex);
        }
        catch (Exception ex) when (IsTransport(ex)) { throw Transport("save card", ex); }
        catch (JsonException ex) { throw BrokenBody("save card", ex); }
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(id: vaultId, ct: cancellationToken);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            ex.Error.TryGetRawError(out var raw);
            // Already gone at PayPal => treat delete as idempotently successful.
            if (raw is not null && (int)raw.StatusCode == (int)HttpStatusCode.NotFound)
                return;

            ex.Error.TryGetError1(out var typed);
            throw GatewayFromTyped("delete saved card", typed?.Message, raw, typed is not null, ex);
        }
        catch (Exception ex) when (IsTransport(ex)) { throw Transport("delete saved card", ex); }
        catch (JsonException ex) { throw BrokenBody("delete saved card", ex); }
    }

    // ---------------------------------------------------------------------------------------------
    // Transaction reporting (reconciliation) — walk every page of the range.
    // ---------------------------------------------------------------------------------------------
    public async Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var records = new List<PayPalTransactionRecord>();
        var startDate = FormatReportDate(from);
        var endDate = FormatReportDate(to);

        int page = 1;
        int totalPages = 1;
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
                    ct: cancellationToken);
            }
            catch (SdkException<RawError> ex)
            {
                // Case B operation — the error model IS RawError; no typed accessors.
                var status = (int)ex.Error.StatusCode;
                _logger.LogError(ex, "PayPal transaction search failed on page {Page} (HTTP {Status})", page, status);
                throw Gateway("search transactions", $"PayPal transaction search failed (HTTP {status}).", status, ex);
            }
            catch (Exception ex) when (IsTransport(ex)) { throw Transport("search transactions", ex); }
            catch (JsonException ex) { throw BrokenBody("search transactions", ex); }

            totalPages = response.TotalPages ?? 1;

            if (response.TransactionDetails is not null)
            {
                foreach (var detail in response.TransactionDetails)
                {
                    var info = detail.TransactionInfo;
                    if (info?.TransactionId is null)
                        continue;

                    records.Add(new PayPalTransactionRecord(
                        info.TransactionId,
                        ParseMoney(info.TransactionAmount),
                        info.TransactionAmount?.CurrencyCode,
                        info.TransactionStatus,
                        ParseDate(info.TransactionUpdatedDate ?? info.TransactionInitiationDate)));
                }
            }

            page++;
        }
        while (page <= totalPages);

        return records;
    }

    // ---------------------------------------------------------------------------------------------
    // Mapping / helpers
    // ---------------------------------------------------------------------------------------------
    private static CardRequest BuildCardRequest(PayPalPaymentSource source)
    {
        if (source.VaultId is not null)
            return new CardRequest { VaultId = source.VaultId };

        var card = source.Card!;
        return new CardRequest
        {
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            Name = card.CardholderName,
            BillingAddress = BuildAddress(card.BillingAddress)
        };
    }

    private static Address BuildAddress(PaymentCardBillingAddress billing) =>
        new()
        {
            CountryCode = billing.CountryCode,
            AddressLine1 = billing.AddressLine1,
            AddressLine2 = billing.AddressLine2,
            AdminArea1 = billing.AdminArea1,
            AdminArea2 = billing.AdminArea2,
            PostalCode = billing.PostalCode
        };

    private static (string? AuthorizationId, string? Status, DateTimeOffset? ExpiresAt) ExtractAuthorization(IReadOnlyList<PurchaseUnit>? purchaseUnits)
    {
        if (purchaseUnits is null)
            return (null, null, null);

        foreach (var pu in purchaseUnits)
        {
            var authorizations = pu.Payments?.Authorizations;
            if (authorizations is null)
                continue;

            foreach (var authorization in authorizations)
            {
                if (authorization.Id is not null)
                    return (authorization.Id, authorization.Status?.Value, ParseDate(authorization.ExpirationTime));
            }
        }

        return (null, null, null);
    }

    private static bool RequiresApproval(string? orderStatus, IReadOnlyList<LinkDescription>? links)
    {
        if (orderStatus is not null && orderStatus == OrderStatus.PayerActionRequired.Value)
            return true;

        if (links is not null)
        {
            foreach (var link in links)
            {
                if (string.Equals(link.Rel, "payer-action", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(link.Rel, "approve", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    private static readonly HashSet<string> ZeroDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "JPY", "KRW", "VND", "CLP", "ISK", "HUF", "TWD"
    };

    private static string FormatAmount(decimal amount, string currencyCode)
    {
        var format = ZeroDecimalCurrencies.Contains(currencyCode) ? "F0" : "F2";
        return amount.ToString(format, CultureInfo.InvariantCulture);
    }

    private static string FormatReportDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss-0000", CultureInfo.InvariantCulture);

    private static decimal? ParseMoney(Money? money)
    {
        if (money?.Value is null)
            return null;
        return decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt) ? dt : null;
    }

    private static string FormatDetail(string? message) =>
        string.IsNullOrWhiteSpace(message) ? string.Empty : $" ({message})";

    // --- error boundary ---
    private static bool IsTransport(Exception ex) => ex is HttpRequestException or TaskCanceledException or OperationCanceledException;

    private static int MapStatus(int http) => http switch
    {
        400 or 404 or 409 or 422 => http,
        _ => 502
    };

    private PaymentGatewayException GatewayFromTyped(string operation, string? providerMessage, RawError? raw, bool typedPresent, Exception inner)
    {
        int http = raw is not null ? (int)raw.StatusCode : (typedPresent ? 422 : 0);
        return Gateway(operation, providerMessage, MapStatus(http), inner);
    }

    private PaymentGatewayException Gateway(string operation, string? providerMessage, int clientStatus, Exception? inner)
    {
        _logger.LogError(inner, "PayPal {Operation} failed (client status {Status}): {Message}", operation, clientStatus, providerMessage);
        var message = $"PayPal {operation} failed.{FormatDetail(providerMessage)}";
        return new PaymentGatewayException(message, clientStatus, inner);
    }

    private PaymentGatewayException Transport(string operation, Exception inner)
    {
        _logger.LogError(inner, "PayPal {Operation} could not reach the provider", operation);
        return new PaymentGatewayException($"PayPal is currently unreachable; please retry {operation}.", 502, inner);
    }

    private PaymentGatewayException BrokenBody(string operation, Exception inner)
    {
        _logger.LogError(inner, "PayPal {Operation} returned a response that could not be processed", operation);
        return new PaymentGatewayException($"PayPal returned a response for {operation} that could not be processed.", 502, inner);
    }

    private static PaymentChallengeRequiredException ChallengeRequired() =>
        new("The card payment requires the shopper to approve it in a browser, which this integration does not support. Use a card that authorizes without a challenge (e.g. PayPal's sandbox test Visa).");
}
