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
using Microsoft.Extensions.Logging;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// PayPal implementation of <see cref="IPaymentGateway"/> over the PayPalServerSdk.
/// Every SDK failure is translated here into <see cref="PaymentGatewayException"/>;
/// no SDK exception type crosses this boundary. Card details flow through but are
/// never logged or persisted.
/// </summary>
public class PayPalPaymentGateway : IPaymentGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(45);

    private readonly PayPalServerSdkClient _client;
    private readonly ILogger<PayPalPaymentGateway> _logger;

    public PayPalPaymentGateway(PayPalServerSdkClient client, ILogger<PayPalPaymentGateway> logger)
    {
        _client = client;
        _logger = logger;
    }

    public Task<GatewayAuthorization> AuthorizeCardPaymentAsync(int orderId, decimal amount, string currency,
        CardDetails card, string idempotencyKey, CancellationToken ct = default)
    {
        var cardRequest = new CardRequest
        {
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            Name = card.CardholderName,
            BillingAddress = BuildBillingAddress(card)
        };
        return AuthorizeAsync(orderId, amount, currency, cardRequest, idempotencyKey, ct);
    }

    public Task<GatewayAuthorization> AuthorizeVaultedCardPaymentAsync(int orderId, decimal amount, string currency,
        string vaultTokenId, string idempotencyKey, CancellationToken ct = default)
    {
        var cardRequest = new CardRequest { VaultId = vaultTokenId };
        return AuthorizeAsync(orderId, amount, currency, cardRequest, idempotencyKey, ct);
    }

    private async Task<GatewayAuthorization> AuthorizeAsync(int orderId, decimal amount, string currency,
        CardRequest cardRequest, string idempotencyKey, CancellationToken ct)
    {
        return await Bounded(async ctk =>
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
                            CurrencyCode = currency,
                            Value = FormatMoney(amount)
                        },
                        ReferenceId = $"order-{orderId}",
                        InvoiceId = $"order-{orderId}",
                        CustomId = orderId.ToString(CultureInfo.InvariantCulture),
                        Description = $"eShopOnWeb order {orderId}"
                    }
                },
                PaymentSource = new PaymentSource { Card = cardRequest }
            };

            Order order;
            try
            {
                order = await _client.Orders.CreateOrder(
                    payPalMockResponse: null,
                    payPalRequestId: $"{idempotencyKey}-order",
                    payPalPartnerAttributionId: null,
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: orderRequest,
                    ct: ctk);
            }
            catch (SdkException<CreateOrderError> ex)
            {
                throw TranslateCreateOrderError(ex);
            }
            catch (JsonException ex) { throw Unreadable(ex); }
            catch (HttpRequestException ex) { throw Unreachable(ex); }

            ThrowIfPayerActionRequired(order.Status, order.Links);

            OrderAuthorizeResponse authorizeResponse;
            try
            {
                authorizeResponse = await _client.Orders.AuthorizeOrder(
                    id: order.Id,
                    payPalMockResponse: null,
                    payPalRequestId: idempotencyKey,
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: null,
                    prefer: "return=representation",
                    ct: ctk);
            }
            catch (SdkException<AuthorizeOrderError> ex)
            {
                throw TranslateAuthorizeOrderError(ex);
            }
            catch (JsonException ex) { throw Unreadable(ex); }
            catch (HttpRequestException ex) { throw Unreachable(ex); }

            ThrowIfPayerActionRequired(authorizeResponse.Status, authorizeResponse.Links);

            var authorization = authorizeResponse.PurchaseUnits?
                .Select(p => p.Payments)
                .FirstOrDefault(p => p?.Authorizations?.Count > 0)?
                .Authorizations![0];

            if (authorization?.Id is null)
            {
                throw new PaymentGatewayException(PaymentFailureKind.Unexpected,
                    "PayPal authorized the order but returned no authorization record.");
            }

            if (authorization.Status == AuthorizationStatus.Denied)
            {
                var code = authorization.ProcessorResponse?.ResponseCode?.Value;
                throw new PaymentGatewayException(PaymentFailureKind.Declined,
                    $"PayPal declined the card (processor response {code ?? "unknown"}).",
                    providerErrorName: "DECLINED", providerIssue: code);
            }

            return new GatewayAuthorization(
                order.Id,
                authorization.Id,
                authorization.Status?.Value ?? "UNKNOWN",
                ParseDate(authorization.ExpirationTime),
                authorization.ProcessorResponse?.ResponseCode?.Value);
        }, ct);
    }

    public async Task<GatewayAuthorizationState> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default)
    {
        return await Bounded(async ctk =>
        {
            try
            {
                var authorization = await _client.Payments.GetAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    ct: ctk);

                return new GatewayAuthorizationState(
                    authorization.Id ?? authorizationId,
                    authorization.Status?.Value ?? "UNKNOWN",
                    ParseDate(authorization.ExpirationTime));
            }
            catch (SdkException<GetAuthorizedPaymentError> ex)
            {
                throw TranslateGetAuthorizedPaymentError(ex);
            }
            catch (JsonException ex) { throw Unreadable(ex); }
            catch (HttpRequestException ex) { throw Unreachable(ex); }
        }, ct);
    }

    public async Task<GatewayAuthorizationState> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken ct = default)
    {
        return await Bounded(async ctk =>
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
                    ct: ctk);

                return new GatewayAuthorizationState(
                    authorization.Id ?? authorizationId,
                    authorization.Status?.Value ?? "UNKNOWN",
                    ParseDate(authorization.ExpirationTime));
            }
            catch (SdkException<ReauthorizePaymentError> ex)
            {
                throw TranslateReauthorizeError(ex);
            }
            catch (JsonException ex) { throw Unreadable(ex); }
            catch (HttpRequestException ex) { throw Unreachable(ex); }
        }, ct);
    }

    public async Task<GatewayCapture> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken ct = default)
    {
        return await Bounded(async ctk =>
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
                        Amount = new Money { CurrencyCode = currency, Value = FormatMoney(amount) },
                        FinalCapture = true
                    },
                    prefer: "return=representation",
                    ct: ctk);

                if (capture.Id is null)
                {
                    throw new PaymentGatewayException(PaymentFailureKind.Unexpected,
                        "PayPal captured the payment but returned no capture id.");
                }

                var breakdown = capture.SellerReceivableBreakdown;
                return new GatewayCapture(
                    capture.Id,
                    capture.Status?.Value ?? "UNKNOWN",
                    ParseMoney(breakdown?.GrossAmount),
                    ParseMoney(breakdown?.PaypalFee),
                    ParseMoney(breakdown?.NetAmount));
            }
            catch (SdkException<CaptureAuthorizedPaymentError> ex)
            {
                throw TranslateCaptureError(ex);
            }
            catch (JsonException ex) { throw Unreadable(ex); }
            catch (HttpRequestException ex) { throw Unreachable(ex); }
        }, ct);
    }

    public async Task<string> VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default)
    {
        return await Bounded(async ctk =>
        {
            try
            {
                var authorization = await _client.Payments.VoidPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    payPalRequestId: idempotencyKey,
                    ct: ctk);

                return authorization.Status?.Value ?? "VOIDED";
            }
            catch (SdkException<VoidPaymentError> ex)
            {
                throw TranslateVoidError(ex);
            }
            catch (JsonException ex) { throw Unreadable(ex); }
            catch (HttpRequestException ex) { throw Unreachable(ex); }
        }, ct);
    }

    public async Task<GatewayRefund> RefundCaptureAsync(string captureId, decimal amount, string currency,
        string idempotencyKey, string? noteToPayer = null, CancellationToken ct = default)
    {
        return await Bounded(async ctk =>
        {
            try
            {
                var refund = await _client.Payments.RefundCapturedPayment(
                    captureId: captureId,
                    payPalMockResponse: null,
                    payPalRequestId: idempotencyKey,
                    payPalAuthAssertion: null,
                    body: new RefundRequest
                    {
                        Amount = new Money { CurrencyCode = currency, Value = FormatMoney(amount) },
                        NoteToPayer = noteToPayer
                    },
                    prefer: "return=representation",
                    ct: ctk);

                if (refund.Id is null)
                {
                    throw new PaymentGatewayException(PaymentFailureKind.Unexpected,
                        "PayPal refunded the capture but returned no refund id.");
                }

                return new GatewayRefund(refund.Id, refund.Status?.Value ?? "UNKNOWN", ParseMoney(refund.Amount));
            }
            catch (SdkException<RefundCapturedPaymentError> ex)
            {
                throw TranslateRefundError(ex);
            }
            catch (JsonException ex) { throw Unreadable(ex); }
            catch (HttpRequestException ex) { throw Unreachable(ex); }
        }, ct);
    }

    public async Task<GatewayVaultedCard> VaultCardAsync(string shopperKey, string? payPalCustomerId, CardDetails card,
        string idempotencyKey, CancellationToken ct = default)
    {
        return await Bounded(async ctk =>
        {
            try
            {
                var response = await _client.Vault.CreatePaymentToken(
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
                                BillingAddress = BuildBillingAddress(card)
                            }
                        },
                        Customer = new Customer { Id = payPalCustomerId, MerchantCustomerId = shopperKey }
                    },
                    ct: ctk);

                if (response.Id is null)
                {
                    throw new PaymentGatewayException(PaymentFailureKind.Unexpected,
                        "PayPal vaulted the card but returned no payment token id.");
                }

                var cardEntity = response.PaymentSource?.Card;
                return new GatewayVaultedCard(
                    response.Id,
                    response.Customer?.Id,
                    cardEntity?.Brand?.Value,
                    cardEntity?.LastDigits,
                    cardEntity?.Expiry,
                    cardEntity?.Name);
            }
            catch (SdkException<CreatePaymentTokenError> ex)
            {
                throw TranslateCreatePaymentTokenError(ex);
            }
            catch (JsonException ex) { throw Unreadable(ex); }
            catch (HttpRequestException ex) { throw Unreachable(ex); }
        }, ct);
    }

    public async Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken ct = default)
    {
        await Bounded<object?>(async ctk =>
        {
            try
            {
                await _client.Vault.DeletePaymentToken(id: vaultTokenId, ct: ctk);
                return null;
            }
            catch (SdkException<DeletePaymentTokenError> ex)
            {
                throw TranslateDeletePaymentTokenError(ex);
            }
            catch (JsonException ex) { throw Unreadable(ex); }
            catch (HttpRequestException ex) { throw Unreachable(ex); }
        }, ct);
    }

    public async Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default)
    {
        return await Bounded(async ctk =>
        {
            var results = new List<GatewayTransaction>();
            var page = 1;
            while (true)
            {
                SearchResponse response;
                try
                {
                    response = await _client.TransactionSearch.SearchTransactions(
                        startDate: from.ToString("O"),
                        endDate: to.ToString("O"),
                        transactionId: null,
                        transactionType: null,
                        transactionStatus: null,
                        transactionAmount: null,
                        transactionCurrency: null,
                        paymentInstrumentType: null,
                        storeId: null,
                        terminalId: null,
                        pageSize: 100,
                        page: page,
                        ct: ctk);
                }
                catch (SdkException<RawError> ex)
                {
                    // Case B: the only operation without a typed error model.
                    var status = (int)ex.Error.StatusCode;
                    var kind = status is >= 400 and < 500 ? PaymentFailureKind.Validation : PaymentFailureKind.Unavailable;
                    throw new PaymentGatewayException(kind,
                        $"PayPal transaction search failed with HTTP {status}.");
                }
                catch (JsonException ex) { throw Unreadable(ex); }
                catch (HttpRequestException ex) { throw Unreachable(ex); }

                foreach (var detail in response.TransactionDetails ?? Enumerable.Empty<TransactionDetails>())
                {
                    var info = detail.TransactionInfo;
                    if (info is null)
                    {
                        continue;
                    }

                    results.Add(new GatewayTransaction(
                        info.TransactionId,
                        info.PaypalReferenceId,
                        info.PaypalReferenceIdType?.Value,
                        info.TransactionEventCode,
                        ParseMoney(info.TransactionAmount),
                        info.TransactionAmount?.CurrencyCode,
                        ParseMoney(info.FeeAmount),
                        info.TransactionStatus,
                        info.InvoiceId,
                        info.CustomField,
                        ParseDate(info.TransactionInitiationDate),
                        ParseDate(info.TransactionUpdatedDate)));
                }

                if (page >= (response.TotalPages ?? 1))
                {
                    break;
                }
                page++;
            }

            return (IReadOnlyList<GatewayTransaction>)results;
        }, ct);
    }

    private static Address? BuildBillingAddress(CardDetails card)
    {
        if (string.IsNullOrWhiteSpace(card.CountryCode))
        {
            return null;
        }

        return new Address
        {
            AddressLine1 = card.AddressLine1,
            AdminArea2 = card.City,
            AdminArea1 = card.State,
            PostalCode = card.PostalCode,
            CountryCode = card.CountryCode
        };
    }

    private void ThrowIfPayerActionRequired(OrderStatus? status, IReadOnlyList<LinkDescription>? links)
    {
        var payerAction = status == OrderStatus.PayerActionRequired;
        if (!payerAction && links is not null)
        {
            // Best-effort secondary signal; the status enum is the grounded one.
            payerAction = links.Any(l => string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase));
        }

        if (payerAction)
        {
            throw new PaymentGatewayException(PaymentFailureKind.PayerActionRequired,
                "PayPal requires the shopper to approve this payment in a browser (payer-action/3DS). " +
                "This integration does not build an approval round-trip; the payment was not taken.");
        }
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        try
        {
            return await call(cts.Token);
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // the API caller aborted; nothing to translate
        }
        catch (TaskCanceledException ex)
        {
            throw new PaymentGatewayException(PaymentFailureKind.Unavailable,
                "PayPal did not answer in time; the operation outcome is unknown — reconcile before retrying.",
                innerException: ex);
        }
    }

    private static string FormatMoney(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal? ParseMoney(Money? money) =>
        money?.Value is not null && decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static DateTimeOffset? ParseDate(string? value) =>
        value is not null && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;

    private static string Describe(Error error)
    {
        var issue = error.Details?.FirstOrDefault()?.Issue;
        return issue is null ? error.Name : $"{error.Name} ({issue})";
    }

    private static string Describe(Error1 error)
    {
        var issue = error.Details?.FirstOrDefault()?.Issue;
        return issue is null ? error.Name : $"{error.Name} ({issue})";
    }

    private PaymentGatewayException TranslateCreateOrderError(SdkException<CreateOrderError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            _logger.LogWarning("PayPal CreateOrder rejected: {Name}", error.Name);
            return new PaymentGatewayException(PaymentFailureKind.Validation,
                $"PayPal rejected the order: {Describe(error)}.", error.Name, error.Details?.FirstOrDefault()?.Issue, ex);
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            return RawFailure("create the order", raw, ex);
        }
        return new PaymentGatewayException(PaymentFailureKind.Unexpected, "PayPal rejected the order.", innerException: ex);
    }

    private PaymentGatewayException TranslateAuthorizeOrderError(SdkException<AuthorizeOrderError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            _logger.LogWarning("PayPal AuthorizeOrder rejected: {Name}", error.Name);
            return new PaymentGatewayException(PaymentFailureKind.Declined,
                $"PayPal declined the payment: {Describe(error)}.", error.Name, error.Details?.FirstOrDefault()?.Issue, ex);
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            return RawFailure("authorize the payment", raw, ex);
        }
        return new PaymentGatewayException(PaymentFailureKind.Unexpected, "PayPal declined the payment.", innerException: ex);
    }

    private PaymentGatewayException TranslateGetAuthorizedPaymentError(SdkException<GetAuthorizedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return new PaymentGatewayException(PaymentFailureKind.NotFound,
                $"PayPal could not return the authorization: {Describe(error)}.", error.Name, error.Details?.FirstOrDefault()?.Issue, ex);
        }
        if (ex.Error.TryGetNoContent(out var noContent))
        {
            return RawFailure("read the authorization", noContent, ex);
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            return RawFailure("read the authorization", raw, ex);
        }
        return new PaymentGatewayException(PaymentFailureKind.Unexpected, "PayPal could not return the authorization.", innerException: ex);
    }

    private PaymentGatewayException TranslateReauthorizeError(SdkException<ReauthorizePaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            // Any typed rejection here means the authorization cannot be renewed.
            return new PaymentGatewayException(PaymentFailureKind.AuthorizationNotRenewable,
                $"PayPal refused to renew the authorization: {Describe(error)}.", error.Name, error.Details?.FirstOrDefault()?.Issue, ex);
        }
        if (ex.Error.TryGetNoContent(out var noContent))
        {
            return RawFailure("renew the authorization", noContent, ex);
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            return RawFailure("renew the authorization", raw, ex);
        }
        return new PaymentGatewayException(PaymentFailureKind.Unexpected, "PayPal refused to renew the authorization.", innerException: ex);
    }

    private PaymentGatewayException TranslateCaptureError(SdkException<CaptureAuthorizedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return new PaymentGatewayException(PaymentFailureKind.Conflict,
                $"PayPal could not capture the authorization: {Describe(error)}.", error.Name, error.Details?.FirstOrDefault()?.Issue, ex);
        }
        if (ex.Error.TryGetNoContent(out var noContent))
        {
            return RawFailure("capture the payment", noContent, ex);
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            return RawFailure("capture the payment", raw, ex);
        }
        return new PaymentGatewayException(PaymentFailureKind.Unexpected, "PayPal could not capture the payment.", innerException: ex);
    }

    private PaymentGatewayException TranslateVoidError(SdkException<VoidPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return new PaymentGatewayException(PaymentFailureKind.Conflict,
                $"PayPal could not void the authorization: {Describe(error)}.", error.Name, error.Details?.FirstOrDefault()?.Issue, ex);
        }
        if (ex.Error.TryGetNoContent(out var noContent))
        {
            return RawFailure("void the authorization", noContent, ex);
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            return RawFailure("void the authorization", raw, ex);
        }
        return new PaymentGatewayException(PaymentFailureKind.Unexpected, "PayPal could not void the authorization.", innerException: ex);
    }

    private PaymentGatewayException TranslateRefundError(SdkException<RefundCapturedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return new PaymentGatewayException(PaymentFailureKind.Validation,
                $"PayPal rejected the refund: {Describe(error)}.", error.Name, error.Details?.FirstOrDefault()?.Issue, ex);
        }
        if (ex.Error.TryGetNoContent(out var noContent))
        {
            return RawFailure("refund the payment", noContent, ex);
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            return RawFailure("refund the payment", raw, ex);
        }
        return new PaymentGatewayException(PaymentFailureKind.Unexpected, "PayPal rejected the refund.", innerException: ex);
    }

    private PaymentGatewayException TranslateCreatePaymentTokenError(SdkException<CreatePaymentTokenError> ex)
    {
        if (ex.Error.TryGetError1(out var error))
        {
            _logger.LogWarning("PayPal CreatePaymentToken rejected: {Name}", error.Name);
            return new PaymentGatewayException(PaymentFailureKind.Validation,
                $"PayPal could not save the card: {Describe(error)}.", error.Name, error.Details?.FirstOrDefault()?.Issue, ex);
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            return RawFailure("save the card", raw, ex);
        }
        return new PaymentGatewayException(PaymentFailureKind.Unexpected, "PayPal could not save the card.", innerException: ex);
    }

    private PaymentGatewayException TranslateDeletePaymentTokenError(SdkException<DeletePaymentTokenError> ex)
    {
        if (ex.Error.TryGetError1(out var error))
        {
            return new PaymentGatewayException(PaymentFailureKind.Validation,
                $"PayPal could not delete the saved card: {Describe(error)}.", error.Name, error.Details?.FirstOrDefault()?.Issue, ex);
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            return RawFailure("delete the saved card", raw, ex);
        }
        return new PaymentGatewayException(PaymentFailureKind.Unexpected, "PayPal could not delete the saved card.", innerException: ex);
    }

    private static PaymentGatewayException RawFailure(string operation, RawError raw, Exception ex) =>
        new(PaymentFailureKind.Unavailable,
            $"PayPal could not {operation} (HTTP {(int)raw.StatusCode}); the operation outcome is unknown — reconcile before retrying.",
            innerException: ex);

    private static PaymentGatewayException Unreadable(JsonException ex) =>
        new(PaymentFailureKind.Unexpected,
            "PayPal returned a response that could not be processed.",
            innerException: ex);

    private static PaymentGatewayException Unreachable(HttpRequestException ex) =>
        new(PaymentFailureKind.Unavailable,
            "PayPal could not be reached; the operation outcome is unknown — reconcile before retrying.",
            innerException: ex);
}
