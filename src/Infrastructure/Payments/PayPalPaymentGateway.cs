using System;
using System.Collections.Generic;
using System.Linq;
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

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalPaymentGateway : IPaymentGateway
{
    private readonly PayPalServerSdkClient _client;
    private readonly TimeSpan _callBudget = TimeSpan.FromSeconds(30);

    public PayPalPaymentGateway(PayPalServerSdkClient client, PayPalSettings settings)
    {
        _client = client;
        Currency = settings.Currency;
    }

    public string Currency { get; }

    public async Task<AuthorizePaymentResult> AuthorizeAsync(AuthorizePaymentRequest request, CancellationToken cancellationToken)
    {
        var card = BuildCardRequest(request.Card, request.VaultId);
        var value = PayPalMoneyFormat.ToValue(request.Amount, Currency);
        var orderId = request.OrderId.ToString();

        var body = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new()
                {
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = Currency,
                        Value = value
                    },
                    InvoiceId = $"eShop-{orderId}-{request.IdempotencyKey}",
                    CustomId = orderId
                }
            },
            PaymentSource = new PaymentSource
            {
                Card = card
            }
        };

        try
        {
            var order = await Bounded(
                ct => _client.Orders.CreateOrder(
                    payPalMockResponse: null,
                    payPalRequestId: request.IdempotencyKey,
                    payPalPartnerAttributionId: null,
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: "return=representation",
                    requestOptions: null,
                    ct: ct),
                cancellationToken);

            ThrowIfChallenge(order);

            if (string.IsNullOrEmpty(order.Id) || AuthorizationFrom(order) is null)
            {
                order = await Bounded(
                    ct => _client.Orders.GetOrder(
                        id: order.Id ?? string.Empty,
                        fields: null,
                        payPalMockResponse: null,
                        payPalAuthAssertion: null,
                        requestOptions: null,
                        ct: ct),
                    cancellationToken);
                ThrowIfChallenge(order);
            }

            var authorization = AuthorizationFrom(order);
            if (string.IsNullOrEmpty(order.Id) || authorization is null || string.IsNullOrEmpty(authorization.Id))
            {
                throw new PaymentGatewayException("PayPal authorized the order but did not return an authorization id.", 502);
            }

            return new AuthorizePaymentResult(
                order.Id,
                authorization.Id,
                authorization.Status?.Value,
                ParseTimestamp(authorization.ExpirationTime));
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw MapCreateOrder(ex);
        }
        catch (SdkException<GetOrderError> ex)
        {
            throw MapGetOrder(ex);
        }
        catch (JsonException)
        {
            throw PayPalErrorTranslator.FromStatus(PayPalStatusCaptureHandler.CurrentStatus, IsErrorStatus(PayPalStatusCaptureHandler.CurrentStatus));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentGatewayException("PayPal is unreachable.", 503, inner: ex);
        }
    }

    public async Task<AuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken)
    {
        try
        {
            var auth = await Bounded(
                ct => _client.Payments.GetAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    requestOptions: null,
                    ct: ct),
                cancellationToken);

            return new AuthorizationDetails(auth.Id ?? authorizationId, auth.Status?.Value, ParseTimestamp(auth.ExpirationTime));
        }
        catch (SdkException<GetAuthorizedPaymentError> ex)
        {
            throw MapGetAuthorizedPayment(ex);
        }
        catch (JsonException)
        {
            throw PayPalErrorTranslator.FromStatus(PayPalStatusCaptureHandler.CurrentStatus, IsErrorStatus(PayPalStatusCaptureHandler.CurrentStatus));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentGatewayException("PayPal is unreachable.", 503, inner: ex);
        }
    }

    public async Task<ReauthorizePaymentResult> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var body = new ReauthorizeRequest
        {
            Amount = new Money
            {
                CurrencyCode = currency,
                Value = PayPalMoneyFormat.ToValue(amount, currency)
            }
        };

        try
        {
            var auth = await Bounded(
                ct => _client.Payments.ReauthorizePayment(
                    authorizationId: authorizationId,
                    payPalRequestId: idempotencyKey,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: "return=representation",
                    requestOptions: null,
                    ct: ct),
                cancellationToken);

            if (string.IsNullOrEmpty(auth.Id))
            {
                throw new PaymentGatewayException("PayPal reauthorized the hold but did not return an authorization id.", 502);
            }

            return new ReauthorizePaymentResult(auth.Id, auth.Status?.Value, ParseTimestamp(auth.ExpirationTime));
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            throw MapReauthorize(ex);
        }
        catch (JsonException)
        {
            throw PayPalErrorTranslator.FromStatus(PayPalStatusCaptureHandler.CurrentStatus, IsErrorStatus(PayPalStatusCaptureHandler.CurrentStatus));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentGatewayException("PayPal is unreachable.", 503, inner: ex);
        }
    }

    public async Task<CapturePaymentResult> CaptureAsync(string authorizationId, decimal amount, string idempotencyKey, CancellationToken cancellationToken)
    {
        var body = new CaptureRequest
        {
            Amount = new Money
            {
                CurrencyCode = Currency,
                Value = PayPalMoneyFormat.ToValue(amount, Currency)
            },
            FinalCapture = true
        };

        try
        {
            var captured = await Bounded(
                ct => _client.Payments.CaptureAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalRequestId: idempotencyKey,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: "return=representation",
                    requestOptions: null,
                    ct: ct),
                cancellationToken);

            return ToCaptureResult(captured);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            throw MapCapture(ex);
        }
        catch (JsonException)
        {
            throw PayPalErrorTranslator.FromStatus(PayPalStatusCaptureHandler.CurrentStatus, IsErrorStatus(PayPalStatusCaptureHandler.CurrentStatus));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentGatewayException("PayPal is unreachable.", 503, inner: ex);
        }
    }

    public async Task<CapturePaymentResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken)
    {
        try
        {
            var captured = await Bounded(
                ct => _client.Payments.GetCapturedPayment(
                    captureId: captureId,
                    payPalMockResponse: null,
                    requestOptions: null,
                    ct: ct),
                cancellationToken);

            return ToCaptureResult(captured);
        }
        catch (SdkException<GetCapturedPaymentError> ex)
        {
            throw MapGetCapture(ex);
        }
        catch (JsonException)
        {
            throw PayPalErrorTranslator.FromStatus(PayPalStatusCaptureHandler.CurrentStatus, IsErrorStatus(PayPalStatusCaptureHandler.CurrentStatus));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentGatewayException("PayPal is unreachable.", 503, inner: ex);
        }
    }

    public async Task<VoidPaymentResult> VoidAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        try
        {
            var auth = await Bounded(
                ct => _client.Payments.VoidPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    payPalRequestId: idempotencyKey,
                    prefer: "return=representation",
                    requestOptions: null,
                    ct: ct),
                cancellationToken);

            return new VoidPaymentResult(auth.Status?.Value);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            throw MapVoid(ex);
        }
        catch (JsonException)
        {
            throw PayPalErrorTranslator.FromStatus(PayPalStatusCaptureHandler.CurrentStatus, IsErrorStatus(PayPalStatusCaptureHandler.CurrentStatus));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentGatewayException("PayPal is unreachable.", 503, inner: ex);
        }
    }

    public async Task<RefundPaymentResult> RefundAsync(RefundPaymentRequest request, CancellationToken cancellationToken)
    {
        RefundRequest? body = null;
        if (request.Amount is decimal amount)
        {
            body = new RefundRequest
            {
                Amount = new Money
                {
                    CurrencyCode = request.Currency,
                    Value = PayPalMoneyFormat.ToValue(amount, request.Currency)
                }
            };
        }

        try
        {
            var refund = await Bounded(
                ct => _client.Payments.RefundCapturedPayment(
                    captureId: request.CaptureId,
                    payPalMockResponse: null,
                    payPalRequestId: request.IdempotencyKey,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: "return=representation",
                    requestOptions: null,
                    ct: ct),
                cancellationToken);

            if (string.IsNullOrEmpty(refund.Id))
            {
                throw new PaymentGatewayException("PayPal refunded the capture but did not return a refund id.", 502);
            }

            var refundedAmount = PayPalMoneyFormat.Parse(refund.Amount?.Value) ?? request.Amount ?? 0m;
            return new RefundPaymentResult(refund.Id, refund.Status?.Value, refundedAmount);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            throw MapRefund(ex);
        }
        catch (JsonException)
        {
            throw PayPalErrorTranslator.FromStatus(PayPalStatusCaptureHandler.CurrentStatus, IsErrorStatus(PayPalStatusCaptureHandler.CurrentStatus));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentGatewayException("PayPal is unreachable.", 503, inner: ex);
        }
    }

    public async Task<SavedCardResult> SaveCardAsync(SaveCardRequest request, CancellationToken cancellationToken)
    {
        var card = request.Card;
        var body = new PaymentTokenRequest
        {
            Customer = new Customer
            {
                Id = request.PaypalCustomerId,
                MerchantCustomerId = request.MerchantCustomerId
            },
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Name = card.Name,
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    BillingAddress = BuildAddress(card)
                }
            }
        };

        try
        {
            var token = await Bounded(
                ct => _client.Vault.CreatePaymentToken(
                    payPalRequestId: request.IdempotencyKey,
                    body: body,
                    requestOptions: null,
                    ct: ct),
                cancellationToken);

            if (token.Links?.Any(l => l.Rel == "payer-action") == true)
            {
                throw new PaymentGatewayException(
                    "PayPal required a shopper approval step that this integration does not support.",
                    409,
                    isChallengeRequired: true);
            }

            var cardEntity = token.PaymentSource?.Card;
            return new SavedCardResult(
                token.Id ?? string.Empty,
                token.Customer?.Id,
                cardEntity?.LastDigits,
                cardEntity?.Brand?.Value,
                cardEntity?.Expiry);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            throw MapCreatePaymentToken(ex);
        }
        catch (JsonException)
        {
            throw PayPalErrorTranslator.FromStatus(PayPalStatusCaptureHandler.CurrentStatus, IsErrorStatus(PayPalStatusCaptureHandler.CurrentStatus));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentGatewayException("PayPal is unreachable.", 503, inner: ex);
        }
    }

    public async Task DeleteCardAsync(string paymentTokenId, CancellationToken cancellationToken)
    {
        try
        {
            await Bounded(
                ct => _client.Vault.DeletePaymentToken(
                    id: paymentTokenId,
                    requestOptions: null,
                    ct: ct),
                cancellationToken);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            throw MapDeletePaymentToken(ex);
        }
        catch (JsonException)
        {
            throw PayPalErrorTranslator.FromStatus(PayPalStatusCaptureHandler.CurrentStatus, IsErrorStatus(PayPalStatusCaptureHandler.CurrentStatus));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentGatewayException("PayPal is unreachable.", 503, inner: ex);
        }
    }

    public async Task<IReadOnlyList<TransactionSearchItem>> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var items = new List<TransactionSearchItem>();
        var page = 1;
        var totalPages = 1;

        try
        {
            while (page <= totalPages)
            {
                var currentPage = page;
                var response = await Bounded(
                    ct => _client.TransactionSearch.SearchTransactions(
                        startDate: from.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                        endDate: to.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
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
                        page: currentPage,
                        requestOptions: null,
                        ct: ct),
                    cancellationToken);

                if (response.TransactionDetails is not null)
                {
                    foreach (var detail in response.TransactionDetails)
                    {
                        var info = detail.TransactionInfo;
                        items.Add(new TransactionSearchItem(
                            info?.TransactionId,
                            info?.PaypalReferenceId,
                            info?.TransactionStatus,
                            info?.TransactionAmount?.Value,
                            info?.FeeAmount?.Value,
                            info?.InvoiceId,
                            info?.CustomField,
                            info?.TransactionEventCode,
                            info?.TransactionInitiationDate));
                    }
                }

                totalPages = response.TotalPages ?? 1;
                page++;
            }

            return items;
        }
        catch (SdkException<RawError> ex)
        {
            throw PayPalErrorTranslator.FromRaw(ex.Error);
        }
        catch (JsonException)
        {
            throw PayPalErrorTranslator.FromStatus(PayPalStatusCaptureHandler.CurrentStatus, IsErrorStatus(PayPalStatusCaptureHandler.CurrentStatus));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentGatewayException("PayPal is unreachable.", 503, inner: ex);
        }
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_callBudget);
        return await call(cts.Token);
    }

    private async Task Bounded(Func<CancellationToken, Task> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_callBudget);
        await call(cts.Token);
    }

    private static CardRequest BuildCardRequest(CardPaymentInput? card, string? vaultId)
    {
        if (!string.IsNullOrEmpty(vaultId))
        {
            return new CardRequest
            {
                VaultId = vaultId
            };
        }

        if (card is null)
        {
            throw new OrderPaymentException("Provide card details or a saved payment method id.", 400);
        }

        return new CardRequest
        {
            Name = card.Name,
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            BillingAddress = BuildAddress(card)
        };
    }

    private static Address? BuildAddress(CardPaymentInput card)
    {
        if (string.IsNullOrWhiteSpace(card.CountryCode)
            && string.IsNullOrWhiteSpace(card.AddressLine1)
            && string.IsNullOrWhiteSpace(card.PostalCode))
        {
            return null;
        }

        return new Address
        {
            AddressLine1 = card.AddressLine1,
            AddressLine2 = card.AddressLine2,
            AdminArea2 = card.AdminArea2,
            AdminArea1 = card.AdminArea1,
            PostalCode = card.PostalCode,
            CountryCode = string.IsNullOrWhiteSpace(card.CountryCode) ? "US" : card.CountryCode
        };
    }

    private static AuthorizationWithAdditionalData? AuthorizationFrom(Order order) =>
        order.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();

    private static void ThrowIfChallenge(Order order)
    {
        if (order.Status == OrderStatus.PayerActionRequired
            || order.Links?.Any(l => l.Rel == "payer-action") == true)
        {
            throw new PaymentGatewayException(
                "PayPal required a shopper approval step that this integration does not support.",
                409,
                isChallengeRequired: true);
        }
    }

    private static CapturePaymentResult ToCaptureResult(CapturedPayment captured)
    {
        if (string.IsNullOrEmpty(captured.Id))
        {
            throw new PaymentGatewayException("PayPal captured the payment but did not return a capture id.", 502);
        }

        var breakdown = captured.SellerReceivableBreakdown;
        return new CapturePaymentResult(
            captured.Id,
            captured.Status?.Value,
            PayPalMoneyFormat.Parse(breakdown?.GrossAmount.Value) ?? PayPalMoneyFormat.Parse(captured.Amount?.Value),
            PayPalMoneyFormat.Parse(breakdown?.PaypalFee?.Value),
            PayPalMoneyFormat.Parse(breakdown?.NetAmount?.Value));
    }

    private static DateTimeOffset? ParseTimestamp(string? value)
    {
        if (DateTimeOffset.TryParse(value, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static bool IsErrorStatus(System.Net.HttpStatusCode? status) =>
        status is System.Net.HttpStatusCode code && (int)code >= 400;

    private static PaymentGatewayException MapCreateOrder(SdkException<CreateOrderError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return PayPalErrorTranslator.FromError(error, 400);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return PayPalErrorTranslator.FromRaw(raw);
        }

        return new PaymentGatewayException("PayPal rejected the payment.", 400);
    }

    private static PaymentGatewayException MapGetOrder(SdkException<GetOrderError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return PayPalErrorTranslator.FromError(error, 404);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return PayPalErrorTranslator.FromRaw(raw);
        }

        return new PaymentGatewayException("PayPal could not load the order.", 502);
    }

    private static PaymentGatewayException MapGetAuthorizedPayment(SdkException<GetAuthorizedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return PayPalErrorTranslator.FromError(error, 404);
        }

        if (ex.Error.TryGetNoContent(out var noContent))
        {
            return PayPalErrorTranslator.FromRaw(noContent);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return PayPalErrorTranslator.FromRaw(raw);
        }

        return new PaymentGatewayException("PayPal could not load the authorization.", 502);
    }

    private static PaymentGatewayException MapReauthorize(SdkException<ReauthorizePaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return PayPalErrorTranslator.FromError(error, 422);
        }

        if (ex.Error.TryGetNoContent(out var noContent))
        {
            return PayPalErrorTranslator.FromRaw(noContent);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return PayPalErrorTranslator.FromRaw(raw);
        }

        return new PaymentGatewayException("PayPal could not renew the authorization.", 422);
    }

    private static PaymentGatewayException MapCapture(SdkException<CaptureAuthorizedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            var status = error.Details?.Any(d =>
                string.Equals(d.Issue, "AUTHORIZATION_ALREADY_CAPTURED", StringComparison.OrdinalIgnoreCase)
                || string.Equals(d.Issue, "CAPTURE_ALREADY_EXISTS", StringComparison.OrdinalIgnoreCase)) == true
                ? 409
                : 400;
            return PayPalErrorTranslator.FromError(error, status);
        }

        if (ex.Error.TryGetNoContent(out var noContent))
        {
            return PayPalErrorTranslator.FromRaw(noContent);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return PayPalErrorTranslator.FromRaw(raw);
        }

        return new PaymentGatewayException("PayPal could not capture the authorization.", 400);
    }

    private static PaymentGatewayException MapGetCapture(SdkException<GetCapturedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return PayPalErrorTranslator.FromError(error, 404);
        }

        if (ex.Error.TryGetNoContent(out var noContent))
        {
            return PayPalErrorTranslator.FromRaw(noContent);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return PayPalErrorTranslator.FromRaw(raw);
        }

        return new PaymentGatewayException("PayPal could not load the capture.", 502);
    }

    private static PaymentGatewayException MapVoid(SdkException<VoidPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            var status = 400;
            if (error.Name.Contains("RESOURCE", StringComparison.OrdinalIgnoreCase)
                || error.Details?.Any(d => d.Issue.Contains("ALREADY", StringComparison.OrdinalIgnoreCase)) == true)
            {
                status = 409;
            }

            return PayPalErrorTranslator.FromError(error, status);
        }

        if (ex.Error.TryGetNoContent(out var noContent))
        {
            return PayPalErrorTranslator.FromRaw(noContent);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return PayPalErrorTranslator.FromRaw(raw);
        }

        return new PaymentGatewayException("PayPal could not void the authorization.", 400);
    }

    private static PaymentGatewayException MapRefund(SdkException<RefundCapturedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return PayPalErrorTranslator.FromError(error, 400);
        }

        if (ex.Error.TryGetNoContent(out var noContent))
        {
            return PayPalErrorTranslator.FromRaw(noContent);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return PayPalErrorTranslator.FromRaw(raw);
        }

        return new PaymentGatewayException("PayPal could not refund the capture.", 400);
    }

    private static PaymentGatewayException MapCreatePaymentToken(SdkException<CreatePaymentTokenError> ex)
    {
        if (ex.Error.TryGetError1(out var error))
        {
            return PayPalErrorTranslator.FromError1(error, 400);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return PayPalErrorTranslator.FromRaw(raw);
        }

        return new PaymentGatewayException("PayPal could not save the card.", 400);
    }

    private static PaymentGatewayException MapDeletePaymentToken(SdkException<DeletePaymentTokenError> ex)
    {
        if (ex.Error.TryGetError1(out var error))
        {
            return PayPalErrorTranslator.FromError1(error, 400);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return PayPalErrorTranslator.FromRaw(raw);
        }

        return new PaymentGatewayException("PayPal could not delete the saved card.", 400);
    }
}
