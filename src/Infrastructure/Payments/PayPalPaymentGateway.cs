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
using PayPalAddress = PayPalServerSdk.Models.Address;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// PayPal implementation of the payment gateway, built on the PayPalServerSdk .NET SDK.
/// This class is the integration's error boundary: SDK exceptions, transport failures and
/// unreadable responses are translated here into PaymentGatewayException / PaymentDeclinedException
/// with caller-safe messages. Raw card details flow through to the provider and are never logged.
/// </summary>
public class PayPalPaymentGateway : IPaymentGateway
{
    private readonly PayPalServerSdkClient _client;
    private readonly ILogger<PayPalPaymentGateway> _logger;

    public PayPalPaymentGateway(PayPalServerSdkClient client, ILogger<PayPalPaymentGateway> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<GatewayOrder> CreateOrderAsync(decimal amount, string currency, string referenceId,
        string invoiceId, string customId, string idempotencyKey, CancellationToken ct = default)
    {
        // Two-step direct-card pattern: the payment source is supplied at authorize time, not here.
        var body = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = currency,
                        Value = FormatMoney(amount)
                    },
                    ReferenceId = referenceId,
                    InvoiceId = invoiceId,
                    CustomId = customId,
                    Description = $"eShop order {referenceId}"
                }
            }
        };

        try
        {
            var order = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: ct);

            // Defensive: if the provider already authorized at create time, surface that
            // authorization so the caller can skip the separate authorize call.
            var authorization = order.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();

            return new GatewayOrder(
                order.Id ?? string.Empty,
                order.Status?.Value ?? string.Empty,
                IsPayerActionRequired(order.Status, order.Links),
                authorization is null
                    ? null
                    : new GatewayAuthorization(
                        authorization.Id ?? string.Empty,
                        authorization.Status?.Value ?? string.Empty,
                        ParseDate(authorization.ExpirationTime),
                        authorization.Amount?.Value,
                        authorization.Amount?.CurrencyCode,
                        false));
        }
        catch (SdkException<CreateOrderError> ex)
        {
            if (ex.Error.TryGetError(out var error))
            {
                throw TypedError(error, "create the PayPal order");
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw RawGatewayError("create the PayPal order", raw);
            }
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable("create the PayPal order", ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable("create the PayPal order", ex);
        }
    }

    public async Task<GatewayAuthorization> AuthorizeOrderAsync(string payPalOrderId, CardDetails? card,
        string? vaultPaymentTokenId, string idempotencyKey, CancellationToken ct = default)
    {
        // The payment source is supplied here, at authorize time (the SDK-documented direct-card pattern).
        OrderAuthorizeRequestPaymentSource paymentSource;
        if (vaultPaymentTokenId is not null)
        {
            paymentSource = new OrderAuthorizeRequestPaymentSource
            {
                Card = new CardRequest { VaultId = vaultPaymentTokenId }
            };
        }
        else if (card is not null)
        {
            paymentSource = new OrderAuthorizeRequestPaymentSource
            {
                Card = new CardRequest
                {
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    Name = card.HolderName,
                    BillingAddress = MapAddress(card.BillingAddress)
                }
            };
        }
        else
        {
            throw new PaymentStateException("Payment requires either card details or a saved payment method id.");
        }

        try
        {
            var response = await _client.Orders.AuthorizeOrder(
                id: payPalOrderId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: new OrderAuthorizeRequest { PaymentSource = paymentSource },
                prefer: "return=representation",
                ct: ct);

            var authorization = response.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault()
                ?? throw new PaymentGatewayException("PayPal authorized the order but returned no authorization record.");

            _logger.LogInformation("PayPal authorization {AuthorizationId} for order {PayPalOrderId}: {Amount} {Currency}, status {Status}",
                authorization.Id, payPalOrderId, authorization.Amount?.Value, authorization.Amount?.CurrencyCode, authorization.Status?.Value);

            if (string.Equals(authorization.Status?.Value, "DENIED", StringComparison.OrdinalIgnoreCase))
            {
                throw new PaymentDeclinedException("PayPal declined the card for this payment.");
            }

            return new GatewayAuthorization(
                authorization.Id ?? string.Empty,
                authorization.Status?.Value ?? string.Empty,
                ParseDate(authorization.ExpirationTime),
                authorization.Amount?.Value,
                authorization.Amount?.CurrencyCode,
                IsPayerActionRequired(response.Status, response.Links));
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            if (ex.Error.TryGetError(out var error))
            {
                throw TypedError(error, "authorize the payment");
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw RawGatewayError("authorize the payment", raw);
            }
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable("authorize the payment", ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable("authorize the payment", ex);
        }
    }

    public async Task<GatewayAuthorizationState> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default)
    {
        try
        {
            var authorization = await _client.Payments.GetAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                ct: ct);

            return new GatewayAuthorizationState(
                authorization.Id ?? authorizationId,
                authorization.Status?.Value ?? string.Empty,
                ParseDate(authorization.ExpirationTime));
        }
        catch (SdkException<GetAuthorizedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error))
            {
                throw TypedError(error, "read the payment hold");
            }
            if (ex.Error.TryGetNoContent(out var noContent))
            {
                throw RawGatewayError("read the payment hold", noContent);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw RawGatewayError("read the payment hold", raw);
            }
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable("read the payment hold", ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable("read the payment hold", ex);
        }
    }

    public async Task<GatewayAuthorizationState> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            var authorization = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest
                {
                    Amount = new Money { CurrencyCode = currency, Value = FormatMoney(amount) }
                },
                prefer: "return=representation",
                ct: ct);

            return new GatewayAuthorizationState(
                authorization.Id ?? authorizationId,
                authorization.Status?.Value ?? string.Empty,
                ParseDate(authorization.ExpirationTime));
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error))
            {
                // Every status behind TryGetError on this operation is 4xx: the hold cannot be
                // renewed and the caller (fulfilment) must surface that to an operator.
                throw TypedError(error, "renew the payment hold", providerStatusCode: 422);
            }
            if (ex.Error.TryGetNoContent(out var noContent))
            {
                throw RawGatewayError("renew the payment hold", noContent);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw RawGatewayError("renew the payment hold", raw);
            }
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable("renew the payment hold", ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable("renew the payment hold", ex);
        }
    }

    public async Task<GatewayCapture> CaptureAuthorizationAsync(string authorizationId, string invoiceId,
        string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            var capture = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: new CaptureRequest
                {
                    InvoiceId = invoiceId,
                    FinalCapture = true
                },
                prefer: "return=representation",
                ct: ct);

            var breakdown = capture.SellerReceivableBreakdown;
            _logger.LogInformation("PayPal capture {CaptureId} on authorization {AuthorizationId}: gross {Gross} {Currency}, fee {Fee}, net {Net}, status {Status}",
                capture.Id, authorizationId, breakdown?.GrossAmount?.Value ?? capture.Amount?.Value,
                breakdown?.GrossAmount?.CurrencyCode ?? capture.Amount?.CurrencyCode,
                breakdown?.PaypalFee?.Value, breakdown?.NetAmount?.Value, capture.Status?.Value);
            return new GatewayCapture(
                capture.Id ?? string.Empty,
                capture.Status?.Value ?? string.Empty,
                ParseMoney(breakdown?.GrossAmount) ?? ParseMoney(capture.Amount) ?? 0m,
                ParseMoney(breakdown?.PaypalFee),
                ParseMoney(breakdown?.NetAmount),
                breakdown?.GrossAmount?.CurrencyCode ?? capture.Amount?.CurrencyCode);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error))
            {
                throw TypedError(error, "capture the payment");
            }
            if (ex.Error.TryGetNoContent(out var noContent))
            {
                throw RawGatewayError("capture the payment", noContent);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw RawGatewayError("capture the payment", raw);
            }
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable("capture the payment", ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable("capture the payment", ex);
        }
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            // NB: this operation's parameter order puts payPalAuthAssertion before payPalRequestId —
            // named arguments only.
            await _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: idempotencyKey,
                prefer: "return=representation",
                ct: ct);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error))
            {
                throw TypedError(error, "release the payment hold");
            }
            if (ex.Error.TryGetNoContent(out var noContent))
            {
                throw RawGatewayError("release the payment hold", noContent);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw RawGatewayError("release the payment hold", raw);
            }
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable("release the payment hold", ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable("release the payment hold", ex);
        }
    }

    public async Task<GatewayRefund> RefundCaptureAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, CancellationToken ct = default)
    {
        // Full refund = empty payload; partial refund carries an explicit amount.
        var body = amount is null
            ? new RefundRequest()
            : new RefundRequest
            {
                Amount = new Money { CurrencyCode = currency, Value = FormatMoney(amount.Value) }
            };

        // PayPal answers DUPLICATE_REQUEST_ID when a request with the same PayPal-Request-Id is
        // still being processed (e.g. the SDK's transport retry re-sent after a connection reset
        // while the original was in flight). A short bounded retry rides out that race; a caller
        // repeating the same idempotency key after a 502 remains safe and never refunds twice.
        var retryDelaysSeconds = new[] { 5, 10, 15 };
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var refund = await _client.Payments.RefundCapturedPayment(
                    captureId: captureId,
                    payPalMockResponse: null,
                    payPalRequestId: idempotencyKey,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: "return=representation",
                    ct: ct);

                return new GatewayRefund(
                    refund.Id ?? string.Empty,
                    refund.Status?.Value ?? string.Empty,
                    ParseMoney(refund.Amount),
                    refund.Amount?.CurrencyCode);
            }
            catch (SdkException<RefundCapturedPaymentError> ex)
            {
                if (ex.Error.TryGetError(out var error))
                {
                    var isDuplicateInFlight = error.Name == "DUPLICATE_REQUEST_ID"
                        || error.Details?.Any(d => d.Issue == "DUPLICATE_REQUEST_ID") == true;
                    if (isDuplicateInFlight && attempt < retryDelaysSeconds.Length)
                    {
                        _logger.LogInformation(
                            "PayPal is still processing refund request {IdempotencyKey}; waiting for it to settle (attempt {Attempt}).",
                            idempotencyKey, attempt + 1);
                        await Task.Delay(TimeSpan.FromSeconds(retryDelaysSeconds[attempt]), ct);
                        continue;
                    }
                    throw TypedError(error, "refund the payment");
                }
                if (ex.Error.TryGetNoContent(out var noContent))
                {
                    throw RawGatewayError("refund the payment", noContent);
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw RawGatewayError("refund the payment", raw);
                }
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw Unreachable("refund the payment", ex);
            }
            catch (JsonException ex)
            {
                throw Unprocessable("refund the payment", ex);
            }
        }
    }

    public async Task<GatewayVaultedCard> VaultCardAsync(CardDetails card, string? payPalCustomerId, string merchantCustomerId,
        string idempotencyKey, CancellationToken ct = default)
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
                    Name = card.HolderName,
                    BillingAddress = MapAddress(card.BillingAddress)
                }
            },
            Customer = new Customer
            {
                Id = payPalCustomerId,
                MerchantCustomerId = merchantCustomerId
            }
        };

        try
        {
            var response = await _client.Vault.CreatePaymentToken(
                payPalRequestId: idempotencyKey,
                body: body,
                ct: ct);

            return MapVaultedCard(response);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError1(out var error))
            {
                throw TypedError1(error, "save the card");
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw RawGatewayError("save the card", raw);
            }
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable("save the card", ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable("save the card", ex);
        }
    }

    public async Task<IReadOnlyList<GatewayVaultedCard>> ListVaultedCardsAsync(string payPalCustomerId, CancellationToken ct = default)
    {
        var cards = new List<GatewayVaultedCard>();
        var page = 1;
        var totalPages = 1;
        while (page <= totalPages)
        {
            try
            {
                var response = await _client.Vault.ListCustomerPaymentTokens(
                    customerId: payPalCustomerId,
                    pageSize: 50,
                    page: page,
                    totalRequired: true,
                    ct: ct);

                if (response.PaymentTokens is not null)
                {
                    cards.AddRange(response.PaymentTokens.Select(MapVaultedCard));
                }
                totalPages = response.TotalPages ?? page;
                page++;
            }
            catch (SdkException<ListCustomerPaymentTokensError> ex)
            {
                if (ex.Error.TryGetError1(out var error))
                {
                    throw TypedError1(error, "list the saved cards");
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw RawGatewayError("list the saved cards", raw);
                }
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw Unreachable("list the saved cards", ex);
            }
            catch (JsonException ex)
            {
                throw Unprocessable("list the saved cards", ex);
            }
        }
        return cards;
    }

    public async Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken ct = default)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(id: paymentTokenId, ct: ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError1(out var error))
            {
                throw TypedError1(error, "delete the saved card");
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw RawGatewayError("delete the saved card", raw);
            }
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable("delete the saved card", ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable("delete the saved card", ex);
        }
    }

    public async Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default)
    {
        var transactions = new List<GatewayTransaction>();
        var page = 1;
        var totalPages = 1;
        while (page <= totalPages)
        {
            try
            {
                var response = await _client.TransactionSearch.SearchTransactions(
                    startDate: from.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                    endDate: to.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
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

                if (response.TransactionDetails is not null)
                {
                    transactions.AddRange(response.TransactionDetails.Select(MapTransaction));
                }
                totalPages = response.TotalPages ?? page;
                page++;
            }
            catch (SdkException<RawError> ex)
            {
                // Transaction search is the SDK's only Case-B operation: the error IS the raw body.
                // PayPal answers 404 "Data for the given start date is not available." when the
                // range has no reportable transactions yet (sandbox reporting lags live activity);
                // that is an empty report, not a failure.
                if ((int)ex.Error.StatusCode == 404)
                {
                    var notFoundBody = ex.Error.ReadAsString();
                    if (notFoundBody?.Contains("not available", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        _logger.LogInformation("PayPal has no transaction data for the requested range yet; returning an empty report.");
                        return transactions;
                    }
                    throw new PaymentGatewayException(
                        "PayPal could not search PayPal transactions (HTTP 404).", 404);
                }
                throw RawGatewayError("search PayPal transactions", ex.Error);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw Unreachable("search PayPal transactions", ex);
            }
            catch (JsonException ex)
            {
                throw Unprocessable("search PayPal transactions", ex);
            }
        }
        return transactions;
    }

    private static PayPalAddress? MapAddress(BillingAddress? address)
    {
        if (address is null)
        {
            return null;
        }
        return new PayPalAddress
        {
            CountryCode = address.CountryCode,
            AddressLine1 = address.AddressLine1,
            AddressLine2 = address.AddressLine2,
            AdminArea2 = address.City,
            AdminArea1 = address.State,
            PostalCode = address.PostalCode
        };
    }

    private static GatewayVaultedCard MapVaultedCard(PaymentTokenResponse token)
    {
        var card = token.PaymentSource?.Card;
        return new GatewayVaultedCard(
            token.Id ?? string.Empty,
            token.Customer?.Id,
            card?.Brand?.Value,
            card?.LastDigits,
            card?.Expiry,
            card?.Name);
    }

    private static GatewayTransaction MapTransaction(TransactionDetails details)
    {
        var info = details.TransactionInfo;
        return new GatewayTransaction(
            info?.TransactionId,
            info?.PaypalReferenceId,
            info?.PaypalReferenceIdType?.Value,
            info?.TransactionEventCode,
            info?.TransactionStatus,
            ParseMoney(info?.TransactionAmount),
            ParseMoney(info?.FeeAmount),
            info?.TransactionAmount?.CurrencyCode,
            info?.InvoiceId,
            info?.CustomField,
            ParseDate(info?.TransactionUpdatedDate));
    }

    private static bool IsPayerActionRequired(OrderStatus? status, IReadOnlyList<LinkDescription>? links)
    {
        if (status == OrderStatus.PayerActionRequired)
        {
            return true;
        }
        return links?.Any(l => string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static string FormatMoney(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal? ParseMoney(Money? money)
        => money?.Value is not null && decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static DateTimeOffset? ParseDate(string? value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;

    private PaymentGatewayException TypedError(Error error, string action, int? providerStatusCode = null)
    {
        var issues = error.Details is null
            ? null
            : string.Join("; ", error.Details.Select(d => d.Issue));
        _logger.LogWarning("PayPal failed to {Action}: {Name} {Message} ({Issues}), debug id {DebugId}",
            action, error.Name, error.Message, issues, error.DebugId);
        return new PaymentGatewayException(
            $"PayPal could not {action}: {error.Message}",
            providerStatusCode,
            error.DebugId);
    }

    private PaymentGatewayException TypedError1(Error1 error, string action)
    {
        _logger.LogWarning("PayPal failed to {Action}: {Name} {Message}, debug id {DebugId}",
            action, error.Name, error.Message, error.DebugId);
        return new PaymentGatewayException(
            $"PayPal could not {action}: {error.Message}",
            null,
            error.DebugId);
    }

    private PaymentGatewayException RawGatewayError(string action, RawError raw)
    {
        _logger.LogWarning("PayPal failed to {Action}: HTTP {Status} {Body}",
            action, (int)raw.StatusCode, raw.ReadAsString());
        return new PaymentGatewayException(
            $"PayPal could not {action} (HTTP {(int)raw.StatusCode}).",
            (int)raw.StatusCode);
    }

    private PaymentGatewayException Unreachable(string action, Exception ex)
    {
        _logger.LogWarning(ex, "PayPal was unreachable while trying to {Action}.", action);
        return new PaymentGatewayException(
            $"The payment provider could not be reached while trying to {action}. If this was a money-moving operation, verify the order state before retrying.",
            null, null, ex);
    }

    private PaymentGatewayException Unprocessable(string action, JsonException ex)
    {
        _logger.LogWarning(ex, "PayPal returned an unreadable response while trying to {Action}.", action);
        return new PaymentGatewayException(
            $"The payment provider returned a response that could not be processed while trying to {action}.",
            null, null, ex);
    }
}
