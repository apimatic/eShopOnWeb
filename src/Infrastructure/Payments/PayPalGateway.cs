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
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.Extensions.Logging;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using PayPalAddress = PayPalServerSdk.Models.Address;
using PayPalOrder = PayPalServerSdk.Models.Order;
using PayPalOrderStatus = PayPalServerSdk.Models.Enums.OrderStatus;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// PayPal implementation of the payment gateway. Full card details pass through
/// here to PayPal only — they are never persisted and never logged.
/// </summary>
public class PayPalGateway : IPaymentGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(90);

    private readonly PayPalServerSdkClient _client;
    private readonly ILogger<PayPalGateway> _logger;

    public PayPalGateway(PayPalServerSdkClient client, ILogger<PayPalGateway> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<AuthorizationResult> AuthorizeAsync(string? vaultTokenId, CardDetails? card, decimal amount, string currency,
        string referenceId, string idempotencyKey, CancellationToken ct = default)
    {
        return await Bounded(async token =>
        {
            var orderRequest = new OrderRequest
            {
                Intent = CheckoutPaymentIntent.Authorize,
                PurchaseUnits = new List<PurchaseUnitRequest>
                {
                    new PurchaseUnitRequest
                    {
                        Amount = new AmountWithBreakdown { CurrencyCode = currency, Value = Format(amount) },
                        ReferenceId = referenceId
                    }
                },
                PaymentSource = BuildCardPaymentSource(vaultTokenId, card)
            };

            PayPalOrder created;
            try
            {
                created = await _client.Orders.CreateOrder(
                    payPalMockResponse: null,
                    payPalRequestId: idempotencyKey,
                    payPalPartnerAttributionId: null,
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: orderRequest,
                    prefer: "return=representation",
                    requestOptions: null,
                    ct: token);
            }
            catch (SdkException<CreateOrderError> ex)
            {
                throw TranslateOrderWriteError(ex.Error, "create the PayPal order");
            }

            RejectIfPayerActionRequired(created.Status, created.Links);

            // With a card payment_source, PayPal authorizes the card at order-create time and
            // embeds the authorization in the create response; a separate authorize call on an
            // already-authorized order is rejected (ORDER_ALREADY_AUTHORIZED). Only call
            // authorize when the create response carries no authorization.
            var authorization = created.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
            if (authorization is null)
            {
                OrderAuthorizeResponse authorized;
                try
                {
                    authorized = await _client.Orders.AuthorizeOrder(
                        id: created.Id!,
                        payPalMockResponse: null,
                        payPalRequestId: idempotencyKey + "-authorize",
                        payPalClientMetadataId: null,
                        payPalAuthAssertion: null,
                        body: null,
                        prefer: "return=representation",
                        requestOptions: null,
                        ct: token);
                }
                catch (SdkException<AuthorizeOrderError> ex)
                {
                    throw TranslateOrderWriteError(ex.Error, "authorize the payment");
                }

                RejectIfPayerActionRequired(authorized.Status, authorized.Links);
                authorization = authorized.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
            }

            if (authorization?.Id is null)
            {
                throw new PaymentGatewayException("PayPal authorized the order but returned no authorization id.");
            }
            if (authorization.Status == AuthorizationStatus.Denied)
            {
                throw new PaymentDeclinedException("PayPal declined the card for this payment.");
            }

            return new AuthorizationResult(created.Id!, authorization.Id,
                authorization.Status?.Value ?? "UNKNOWN", ParseDate(authorization.ExpirationTime));
        }, ct);
    }

    public async Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken ct = default)
    {
        return await Bounded(async token =>
        {
            CapturedPayment capture;
            try
            {
                capture = await _client.Payments.CaptureAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalRequestId: idempotencyKey,
                    payPalAuthAssertion: null,
                    body: new CaptureRequest
                    {
                        Amount = new Money { CurrencyCode = currency, Value = Format(amount) },
                        FinalCapture = true
                    },
                    prefer: "return=representation",
                    requestOptions: null,
                    ct: token);
            }
            catch (SdkException<CaptureAuthorizedPaymentError> ex)
            {
                throw TranslatePaymentsError(ex.Error, "capture the payment");
            }

            var status = capture.Status?.Value ?? "UNKNOWN";
            if (capture.Status == CaptureStatus.Declined || capture.Status == CaptureStatus.Failed)
            {
                throw new PaymentDeclinedException($"PayPal could not capture the payment (status {status}).");
            }

            var breakdown = capture.SellerReceivableBreakdown;
            return new CaptureResult(
                capture.Id!,
                status,
                ParseMoney(breakdown?.GrossAmount ?? capture.Amount, amount),
                breakdown?.PaypalFee is null ? null : ParseMoney(breakdown.PaypalFee, 0m),
                breakdown?.NetAmount is null ? null : ParseMoney(breakdown.NetAmount, 0m),
                currency);
        }, ct);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default)
    {
        await Bounded(async token =>
        {
            try
            {
                await _client.Payments.VoidPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    payPalRequestId: idempotencyKey,
                    prefer: "return=representation",
                    requestOptions: null,
                    ct: token);
            }
            catch (SdkException<VoidPaymentError> ex)
            {
                throw TranslatePaymentsError(ex.Error, "void the authorization");
            }
        }, ct);
    }

    public async Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken ct = default)
    {
        return await Bounded(async token =>
        {
            PaymentAuthorization renewed;
            try
            {
                renewed = await _client.Payments.ReauthorizePayment(
                    authorizationId: authorizationId,
                    payPalRequestId: idempotencyKey,
                    payPalAuthAssertion: null,
                    body: new ReauthorizeRequest
                    {
                        Amount = new Money { CurrencyCode = currency, Value = Format(amount) }
                    },
                    prefer: "return=representation",
                    requestOptions: null,
                    ct: token);
            }
            catch (SdkException<ReauthorizePaymentError> ex)
            {
                // Any typed rejection here (400/401/403/404/422) means PayPal will not renew this hold.
                if (ex.Error.TryGetError(out var error))
                {
                    throw new AuthorizationNotRenewableException(
                        $"The authorization can no longer be renewed (PayPal: {error.Name}). " +
                        "Ask the shopper to pay again so a new authorization can be created.", error.DebugId);
                }
                if (ex.Error.TryGetNoContent(out var noContent))
                {
                    throw new PaymentGatewayException("PayPal failed to reauthorize the payment.",
                        (int)noContent.StatusCode);
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw new PaymentGatewayException("PayPal failed to reauthorize the payment.",
                        (int)raw.StatusCode);
                }
                throw new PaymentGatewayException("PayPal failed to reauthorize the payment.");
            }

            return new AuthorizationResult(string.Empty, renewed.Id!,
                renewed.Status?.Value ?? "UNKNOWN", ParseDate(renewed.ExpirationTime));
        }, ct);
    }

    public async Task<RefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, CancellationToken ct = default)
    {
        return await Bounded(async token =>
        {
            Refund refund;
            try
            {
                refund = await _client.Payments.RefundCapturedPayment(
                    captureId: captureId,
                    payPalMockResponse: null,
                    payPalRequestId: idempotencyKey,
                    payPalAuthAssertion: null,
                    body: amount.HasValue
                        ? new RefundRequest { Amount = new Money { CurrencyCode = currency, Value = Format(amount.Value) } }
                        : new RefundRequest(),
                    prefer: "return=representation",
                    requestOptions: null,
                    ct: token);
            }
            catch (SdkException<RefundCapturedPaymentError> ex)
            {
                throw TranslatePaymentsError(ex.Error, "refund the payment");
            }

            return new RefundResult(refund.Id!, refund.Status?.Value ?? "UNKNOWN",
                refund.Amount is null ? (amount ?? 0m) : ParseMoney(refund.Amount, amount ?? 0m), currency);
        }, ct);
    }

    public async Task<VaultedCardResult> VaultCardAsync(string buyerId, CardDetails card, string idempotencyKey, CancellationToken ct = default)
    {
        return await Bounded(async token =>
        {
            SetupTokenResponse setupToken;
            try
            {
                setupToken = await _client.Vault.CreateSetupToken(
                    payPalRequestId: idempotencyKey,
                    body: new SetupTokenRequest
                    {
                        Customer = new Customer { MerchantCustomerId = buyerId },
                        PaymentSource = new SetupTokenRequestPaymentSource
                        {
                            Card = new SetupTokenRequestCard
                            {
                                Number = card.Number,
                                Expiry = card.Expiry,
                                SecurityCode = card.SecurityCode,
                                Name = card.CardholderName,
                                BillingAddress = MapAddress(card.BillingAddress)
                            }
                        }
                    },
                    requestOptions: null,
                    ct: token);
            }
            catch (SdkException<CreateSetupTokenError> ex)
            {
                throw TranslateVaultError(ex.Error, "save the card");
            }

            if (setupToken.Status == PaymentTokenStatus.PayerActionRequired)
            {
                throw new PaymentDeclinedException(
                    "PayPal requires the shopper to verify this card in a browser, which this integration does not support.");
            }

            PaymentTokenResponse paymentToken;
            try
            {
                paymentToken = await _client.Vault.CreatePaymentToken(
                    payPalRequestId: idempotencyKey + "-payment-token",
                    body: new PaymentTokenRequest
                    {
                        Customer = new Customer { MerchantCustomerId = buyerId },
                        PaymentSource = new PaymentTokenRequestPaymentSource
                        {
                            Token = new VaultTokenRequest
                            {
                                Id = setupToken.Id!,
                                Type = VaultTokenRequestType.SetupToken
                            }
                        }
                    },
                    requestOptions: null,
                    ct: token);
            }
            catch (SdkException<CreatePaymentTokenError> ex)
            {
                throw TranslateVaultError(ex.Error, "save the card");
            }

            var cardEntity = paymentToken.PaymentSource?.Card;
            return new VaultedCardResult(paymentToken.Id!, cardEntity?.Brand?.Value,
                cardEntity?.LastDigits, cardEntity?.Expiry, cardEntity?.Name);
        }, ct);
    }

    public async Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken ct = default)
    {
        await Bounded(async token =>
        {
            try
            {
                await _client.Vault.DeletePaymentToken(id: vaultTokenId, ct: token);
            }
            catch (SdkException<DeletePaymentTokenError> ex)
            {
                if (ex.Error.TryGetError1(out var error))
                {
                    throw new PaymentGatewayException($"PayPal refused to delete the saved card ({error.Name}).", debugId: error.DebugId);
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw new PaymentGatewayException("PayPal failed to delete the saved card.", (int)raw.StatusCode);
                }
                throw new PaymentGatewayException("PayPal failed to delete the saved card.");
            }
        }, ct);
    }

    public async Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        return await Bounded(async token =>
        {
            var transactions = new List<GatewayTransaction>();
            var page = 1;
            var totalPages = 1;
            do
            {
                SearchResponse response;
                try
                {
                    response = await _client.TransactionSearch.SearchTransactions(
                        startDate: FormatReportDate(from),
                        endDate: FormatReportDate(to),
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
                        requestOptions: null,
                        ct: token);
                }
                catch (SdkException<RawError> ex)
                {
                    throw new PaymentGatewayException("PayPal failed to return the transaction report.",
                        (int)ex.Error.StatusCode);
                }

                totalPages = response.TotalPages ?? 1;
                foreach (var detail in response.TransactionDetails ?? Enumerable.Empty<TransactionDetails>())
                {
                    var info = detail.TransactionInfo;
                    if (info?.TransactionId is null)
                    {
                        continue;
                    }

                    transactions.Add(new GatewayTransaction(
                        info.TransactionId,
                        null,
                        info.TransactionStatus,
                        info.TransactionAmount is null ? null : ParseMoney(info.TransactionAmount, 0m),
                        info.TransactionAmount?.CurrencyCode,
                        info.FeeAmount is null ? null : ParseMoney(info.FeeAmount, 0m),
                        info.PaypalReferenceId,
                        ParseDate(info.TransactionInitiationDate),
                        ParseDate(info.TransactionUpdatedDate)));
                }

                page++;
            }
            while (page <= totalPages);

            return (IReadOnlyList<GatewayTransaction>)transactions;
        }, ct);
    }

    private static PaymentSource BuildCardPaymentSource(string? vaultTokenId, CardDetails? card)
    {
        if (vaultTokenId is not null)
        {
            return new PaymentSource { Card = new CardRequest { VaultId = vaultTokenId } };
        }
        if (card is null)
        {
            throw new PaymentGatewayException("A payment requires either a vaulted card or full card details.");
        }

        return new PaymentSource
        {
            Card = new CardRequest
            {
                Number = card.Number,
                Expiry = card.Expiry,
                SecurityCode = card.SecurityCode,
                Name = card.CardholderName,
                BillingAddress = MapAddress(card.BillingAddress)
            }
        };
    }

    private static PayPalAddress? MapAddress(CardBillingAddress? address)
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

    private static void RejectIfPayerActionRequired(PayPalOrderStatus? status, IReadOnlyList<LinkDescription>? links)
    {
        if (status == PayPalOrderStatus.PayerActionRequired ||
            (links?.Any(l => l.Rel == "payer-action") ?? false))
        {
            throw new PaymentDeclinedException(
                "PayPal requires the shopper to approve this payment in a browser (3-D Secure challenge), " +
                "which this integration does not support.");
        }
    }

    private Exception TranslateOrderWriteError(ApiError error, string action)
    {
        if (error.TryGetRawError(out var raw))
        {
            return new PaymentGatewayException($"PayPal could not {action}.", (int)raw.StatusCode);
        }
        return new PaymentGatewayException($"PayPal could not {action}.");
    }

    private Exception TranslateOrderWriteError(CreateOrderError error, string action)
    {
        if (error.TryGetError(out var typed))
        {
            var issues = typed.Details is null ? string.Empty
                : string.Join("; ", typed.Details.Select(d => $"{d.Issue}: {d.Description}"));
            _logger.LogWarning("PayPal rejected order creation: {Name} [{Issues}] (debug id {DebugId})", typed.Name, issues, typed.DebugId);
            return new PaymentDeclinedException($"PayPal could not {action}: {typed.Message} {issues} (debug id {typed.DebugId}).");
        }
        return TranslateOrderWriteError((ApiError)error, action);
    }

    private Exception TranslateOrderWriteError(AuthorizeOrderError error, string action)
    {
        if (error.TryGetError(out var typed))
        {
            var issues = typed.Details is null ? string.Empty
                : string.Join("; ", typed.Details.Select(d => $"{d.Issue}: {d.Description}"));
            _logger.LogWarning("PayPal rejected authorization: {Name} [{Issues}] (debug id {DebugId})", typed.Name, issues, typed.DebugId);
            return new PaymentDeclinedException($"PayPal could not {action}: {typed.Message} {issues} (debug id {typed.DebugId}).");
        }
        return TranslateOrderWriteError((ApiError)error, action);
    }

    private Exception TranslatePaymentsError(ApiError error, string action)
    {
        if (error.TryGetRawError(out var raw))
        {
            return new PaymentGatewayException($"PayPal could not {action}.", (int)raw.StatusCode);
        }
        return new PaymentGatewayException($"PayPal could not {action}.");
    }

    private Exception TranslatePaymentsError(CaptureAuthorizedPaymentError error, string action)
    {
        if (error.TryGetError(out var typed))
        {
            return new PaymentGatewayException($"PayPal could not {action}: {typed.Name}.", debugId: typed.DebugId);
        }
        if (error.TryGetNoContent(out var noContent))
        {
            return new PaymentGatewayException($"PayPal could not {action}.", (int)noContent.StatusCode);
        }
        return TranslatePaymentsError((ApiError)error, action);
    }

    private Exception TranslatePaymentsError(VoidPaymentError error, string action)
    {
        if (error.TryGetError(out var typed))
        {
            return new PaymentGatewayException($"PayPal could not {action}: {typed.Name}.", debugId: typed.DebugId);
        }
        if (error.TryGetNoContent(out var noContent))
        {
            return new PaymentGatewayException($"PayPal could not {action}.", (int)noContent.StatusCode);
        }
        return TranslatePaymentsError((ApiError)error, action);
    }

    private Exception TranslatePaymentsError(RefundCapturedPaymentError error, string action)
    {
        if (error.TryGetError(out var typed))
        {
            return new PaymentGatewayException($"PayPal could not {action}: {typed.Name}.", debugId: typed.DebugId);
        }
        if (error.TryGetNoContent(out var noContent))
        {
            return new PaymentGatewayException($"PayPal could not {action}.", (int)noContent.StatusCode);
        }
        return TranslatePaymentsError((ApiError)error, action);
    }

    private Exception TranslateVaultError(ApiError error, string action)
    {
        if (error.TryGetRawError(out var raw))
        {
            return new PaymentGatewayException($"PayPal could not {action}.", (int)raw.StatusCode);
        }
        return new PaymentGatewayException($"PayPal could not {action}.");
    }

    private Exception TranslateVaultError(CreateSetupTokenError error, string action)
    {
        if (error.TryGetError1(out var typed))
        {
            _logger.LogWarning("PayPal rejected card vaulting: {Name} (debug id {DebugId})", typed.Name, typed.DebugId);
            return new PaymentDeclinedException($"PayPal could not {action}: {typed.Message} (debug id {typed.DebugId}).");
        }
        return TranslateVaultError((ApiError)error, action);
    }

    private Exception TranslateVaultError(CreatePaymentTokenError error, string action)
    {
        if (error.TryGetError1(out var typed))
        {
            return new PaymentGatewayException($"PayPal could not {action}: {typed.Name}.", debugId: typed.DebugId);
        }
        return TranslateVaultError((ApiError)error, action);
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        try
        {
            return await call(cts.Token);
        }
        catch (JsonException ex)
        {
            // A 2xx with a drifted body, or a non-2xx whose body did not match the generated error shape.
            throw new PaymentGatewayException("The payment provider returned a response that could not be processed.",
                innerException: ex);
        }
        catch (HttpRequestException ex)
        {
            throw new PaymentGatewayException("The payment provider could not be reached.", innerException: ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new PaymentGatewayException("The payment provider did not respond in time.", innerException: ex);
        }
    }

    private async Task Bounded(Func<CancellationToken, Task> call, CancellationToken ct)
    {
        await Bounded<object?>(async token =>
        {
            await call(token);
            return null;
        }, ct);
    }

    private static string Format(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    // PayPal's reporting API rejects offsets/fractional seconds ("Invalid date passed").
    private static string FormatReportDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static decimal ParseMoney(Money money, decimal fallback) =>
        decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
}
