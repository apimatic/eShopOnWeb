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
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using PayPalAddress = PayPalServerSdk.Models.Address;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalPaymentGateway : IPaymentGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private readonly PayPalServerSdkClient _client;

    public PayPalPaymentGateway(PayPalServerSdkClient client)
    {
        _client = client;
    }

    public Task<string> CreateAuthorizedOrderAsync(
        decimal amount,
        string currency,
        string invoiceId,
        string customId,
        string payPalRequestId,
        CancellationToken cancellationToken)
    {
        return Bounded(async ct =>
        {
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
                            Value = FormatAmount(amount)
                        },
                        InvoiceId = invoiceId,
                        CustomId = customId
                    }
                }
            };

            try
            {
                var order = await _client.Orders.CreateOrder(
                    payPalMockResponse: null,
                    payPalRequestId: payPalRequestId,
                    payPalPartnerAttributionId: null,
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: "return=representation",
                    requestOptions: null,
                    ct: ct);

                if (string.IsNullOrEmpty(order.Id))
                {
                    throw new PaymentException("PayPal did not return an order id.", 502);
                }

                return order.Id;
            }
            catch (SdkException<CreateOrderError> ex)
            {
                throw MapTypedError(ex.Error, "create order");
            }
        }, cancellationToken);
    }

    public Task<AuthorizationHold> AuthorizeWithCardAsync(
        string payPalOrderId,
        CardPaymentDetails card,
        string payPalRequestId,
        CancellationToken cancellationToken)
    {
        var body = new OrderAuthorizeRequest
        {
            PaymentSource = new OrderAuthorizeRequestPaymentSource
            {
                Card = BuildCardRequest(card)
            }
        };
        return Authorize(payPalOrderId, body, payPalRequestId, cancellationToken);
    }

    public Task<AuthorizationHold> AuthorizeWithVaultIdAsync(
        string payPalOrderId,
        string vaultId,
        string payPalRequestId,
        CancellationToken cancellationToken)
    {
        var body = new OrderAuthorizeRequest
        {
            PaymentSource = new OrderAuthorizeRequestPaymentSource
            {
                Card = new CardRequest { VaultId = vaultId }
            }
        };
        return Authorize(payPalOrderId, body, payPalRequestId, cancellationToken);
    }

    public Task<AuthorizationHold> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken)
    {
        return Bounded(async ct =>
        {
            try
            {
                var auth = await _client.Payments.GetAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    requestOptions: null,
                    ct: ct);
                return MapAuthorization(auth.Id, auth.Status?.Value, auth.ExpirationTime, auth.CreateTime, auth.Amount, authorizationId);
            }
            catch (SdkException<GetAuthorizedPaymentError> ex)
            {
                throw MapGetAuthorizedError(ex);
            }
        }, cancellationToken);
    }

    public Task<AuthorizationHold> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string payPalRequestId,
        CancellationToken cancellationToken)
    {
        return Bounded(async ct =>
        {
            try
            {
                var body = new ReauthorizeRequest
                {
                    Amount = new Money
                    {
                        CurrencyCode = currency,
                        Value = FormatAmount(amount)
                    }
                };

                var auth = await _client.Payments.ReauthorizePayment(
                    authorizationId: authorizationId,
                    payPalRequestId: payPalRequestId,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: "return=representation",
                    requestOptions: null,
                    ct: ct);

                return MapAuthorization(auth.Id, auth.Status?.Value, auth.ExpirationTime, auth.CreateTime, auth.Amount, authorizationId);
            }
            catch (SdkException<ReauthorizePaymentError> ex)
            {
                throw MapReauthorizeError(ex);
            }
        }, cancellationToken);
    }

    public Task<CaptureDetails> CaptureAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string? invoiceId,
        string payPalRequestId,
        CancellationToken cancellationToken)
    {
        return Bounded(async ct =>
        {
            try
            {
                var body = new CaptureRequest
                {
                    Amount = new Money
                    {
                        CurrencyCode = currency,
                        Value = FormatAmount(amount)
                    },
                    FinalCapture = true,
                    InvoiceId = invoiceId
                };

                var captured = await _client.Payments.CaptureAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalRequestId: payPalRequestId,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: "return=representation",
                    requestOptions: null,
                    ct: ct);

                return MapCapture(captured, currency);
            }
            catch (SdkException<CaptureAuthorizedPaymentError> ex)
            {
                throw MapCaptureError(ex);
            }
        }, cancellationToken);
    }

    public Task VoidAuthorizationAsync(string authorizationId, string payPalRequestId, CancellationToken cancellationToken)
    {
        return Bounded(async ct =>
        {
            try
            {
                await _client.Payments.VoidPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    payPalRequestId: payPalRequestId,
                    prefer: "return=representation",
                    requestOptions: null,
                    ct: ct);
            }
            catch (SdkException<VoidPaymentError> ex)
            {
                throw MapVoidError(ex);
            }
        }, cancellationToken);
    }

    public Task<RefundDetails> RefundAsync(
        string captureId,
        decimal? amount,
        string currency,
        string payPalRequestId,
        CancellationToken cancellationToken)
    {
        return Bounded(async ct =>
        {
            try
            {
                RefundRequest? body = null;
                if (amount is decimal refundAmount)
                {
                    body = new RefundRequest
                    {
                        Amount = new Money
                        {
                            CurrencyCode = currency,
                            Value = FormatAmount(refundAmount)
                        }
                    };
                }

                var refund = await _client.Payments.RefundCapturedPayment(
                    captureId: captureId,
                    payPalMockResponse: null,
                    payPalRequestId: payPalRequestId,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: "return=representation",
                    requestOptions: null,
                    ct: ct);

                return MapRefund(refund, currency);
            }
            catch (SdkException<RefundCapturedPaymentError> ex)
            {
                throw MapRefundError(ex);
            }
        }, cancellationToken);
    }

    public Task<VaultedCardDetails> SaveCardAsync(
        string merchantCustomerId,
        CardPaymentDetails card,
        string payPalRequestId,
        CancellationToken cancellationToken)
    {
        return Bounded(async ct =>
        {
            try
            {
                var body = new PaymentTokenRequest
                {
                    Customer = new Customer { MerchantCustomerId = merchantCustomerId },
                    PaymentSource = new PaymentTokenRequestPaymentSource
                    {
                        Card = new PaymentTokenRequestCard
                        {
                            Name = card.Name,
                            Number = card.Number,
                            Expiry = card.Expiry,
                            SecurityCode = card.SecurityCode,
                            BillingAddress = ToPayPalAddress(card.BillingAddress)
                        }
                    }
                };

                var token = await _client.Vault.CreatePaymentToken(
                    payPalRequestId: payPalRequestId,
                    body: body,
                    requestOptions: null,
                    ct: ct);

                if (string.IsNullOrEmpty(token.Id))
                {
                    throw new PaymentException("PayPal did not return a vault token id.", 502);
                }

                var savedCard = token.PaymentSource?.Card;
                return new VaultedCardDetails(
                    token.Id,
                    token.Customer?.Id,
                    savedCard?.LastDigits,
                    savedCard?.Brand?.Value,
                    savedCard?.Expiry,
                    savedCard?.Name);
            }
            catch (SdkException<CreatePaymentTokenError> ex)
            {
                throw MapVaultError(ex.Error, "save card");
            }
        }, cancellationToken);
    }

    public Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken)
    {
        return Bounded(async ct =>
        {
            try
            {
                await _client.Vault.DeletePaymentToken(id: vaultId, requestOptions: null, ct: ct);
            }
            catch (SdkException<DeletePaymentTokenError> ex)
            {
                if (ex.Error.TryGetRawError(out RawError raw) && (int)raw.StatusCode == 404)
                {
                    return;
                }

                throw MapVaultError(ex.Error, "delete saved card");
            }
        }, cancellationToken);
    }

    public Task<IReadOnlyList<PayPalReportedTransaction>> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        return Bounded(async ct =>
        {
            var results = new List<PayPalReportedTransaction>();
            var startDate = FormatSearchDate(from);
            var endDate = FormatSearchDate(to);
            var page = 1;
            int? totalPages = null;

            try
            {
                while (true)
                {
                    var response = await _client.TransactionSearch.SearchTransactions(
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
                        pageSize: 100,
                        page: page,
                        ct: ct);

                    var details = response.TransactionDetails;
                    if (details is { Count: > 0 })
                    {
                        foreach (var row in details)
                        {
                            var info = row.TransactionInfo;
                            if (info is null || string.IsNullOrEmpty(info.TransactionId))
                            {
                                continue;
                            }

                            results.Add(new PayPalReportedTransaction(
                                info.TransactionId,
                                info.InvoiceId,
                                info.CustomField,
                                info.TransactionStatus?.ToString(),
                                ParseAmount(info.TransactionAmount),
                                ParseAmount(info.FeeAmount),
                                info.TransactionInitiationDate));
                        }
                    }
                    else if (totalPages is null)
                    {
                        break;
                    }

                    if (response.TotalPages is int pages)
                    {
                        totalPages = pages;
                        if (page >= pages)
                        {
                            break;
                        }
                    }
                    else if (details is null || details.Count == 0)
                    {
                        break;
                    }

                    page++;
                    if (page > 1000)
                    {
                        break;
                    }
                }
            }
            catch (SdkException<RawError> ex)
            {
                throw FromRaw(ex.Error, "search transactions");
            }

            return (IReadOnlyList<PayPalReportedTransaction>)results;
        }, cancellationToken);
    }

    private Task<AuthorizationHold> Authorize(
        string payPalOrderId,
        OrderAuthorizeRequest body,
        string payPalRequestId,
        CancellationToken cancellationToken)
    {
        return Bounded(async ct =>
        {
            try
            {
                var authorized = await _client.Orders.AuthorizeOrder(
                    id: payPalOrderId,
                    payPalMockResponse: null,
                    payPalRequestId: payPalRequestId,
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: "return=representation",
                    requestOptions: null,
                    ct: ct);

                RejectIfChallengeRequired(authorized);

                var unit = authorized.PurchaseUnits?.FirstOrDefault();
                var auth = unit?.Payments?.Authorizations?.FirstOrDefault();
                if (auth is null || string.IsNullOrEmpty(auth.Id))
                {
                    throw new PaymentException("PayPal did not return an authorization hold.", 502);
                }

                if (auth.Status == AuthorizationStatus.Denied)
                {
                    throw new PaymentException("The card was declined.", 402);
                }

                if (auth.ProcessorResponse?.ResponseCode == ProcessorResponseCode._5120)
                {
                    throw new PaymentException("Insufficient funds.", 402);
                }

                if (auth.ProcessorResponse?.ResponseCode == ProcessorResponseCode._5650)
                {
                    throw ChallengeRequired();
                }

                var status = auth.Status?.Value ?? AuthorizationStatus.Created.Value;
                if (auth.Status != AuthorizationStatus.Created && auth.Status != AuthorizationStatus.Pending)
                {
                    throw new PaymentException($"Authorization ended in status {status}.", 402);
                }

                return new AuthorizationHold(
                    authorized.Id ?? payPalOrderId,
                    auth.Id,
                    status,
                    auth.ExpirationTime,
                    auth.CreateTime,
                    auth.Amount?.CurrencyCode ?? string.Empty,
                    ParseAmount(auth.Amount) ?? 0m);
            }
            catch (SdkException<AuthorizeOrderError> ex)
            {
                throw MapTypedError(ex.Error, "authorize");
            }
        }, cancellationToken);
    }

    private static void RejectIfChallengeRequired(OrderAuthorizeResponse authorized)
    {
        if (authorized.Status == OrderStatus.PayerActionRequired)
        {
            throw ChallengeRequired();
        }

        if (authorized.Links is not null &&
            authorized.Links.Any(l => string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase)))
        {
            throw ChallengeRequired();
        }

        var threeDs = authorized.PaymentSource?.Card?.AuthenticationResult?.ThreeDSecure;
        if (threeDs?.AuthenticationStatus == ParesStatus.C || threeDs?.AuthenticationStatus == ParesStatus.D)
        {
            throw ChallengeRequired();
        }
    }

    private static PaymentException ChallengeRequired() =>
        new("This card requires a shopper challenge that this integration does not support. Use a card that completes without 3-D Secure.", 409);

    private static CardRequest BuildCardRequest(CardPaymentDetails card) =>
        new()
        {
            Name = card.Name,
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            BillingAddress = ToPayPalAddress(card.BillingAddress)
        };

    private static PayPalAddress? ToPayPalAddress(CardBillingAddress? address)
    {
        if (address is null)
        {
            return null;
        }

        return new PayPalAddress
        {
            CountryCode = address.CountryCode,
            AddressLine1 = address.AddressLine1,
            AdminArea2 = address.AdminArea2,
            AdminArea1 = address.AdminArea1,
            PostalCode = address.PostalCode
        };
    }

    private static AuthorizationHold MapAuthorization(
        string? id,
        string? status,
        string? expirationTime,
        string? createTime,
        Money? amount,
        string fallbackId)
    {
        if (string.IsNullOrEmpty(id))
        {
            throw new PaymentException("PayPal did not return an authorization id.", 502);
        }

        return new AuthorizationHold(
            fallbackId,
            id,
            status ?? string.Empty,
            expirationTime,
            createTime,
            amount?.CurrencyCode ?? string.Empty,
            ParseAmount(amount) ?? 0m);
    }

    private static CaptureDetails MapCapture(CapturedPayment captured, string currency)
    {
        if (string.IsNullOrEmpty(captured.Id))
        {
            throw new PaymentException("PayPal did not return a capture id.", 502);
        }

        var breakdown = captured.SellerReceivableBreakdown;
        var capturedAmount = ParseAmount(captured.Amount) ?? ParseAmount(breakdown?.GrossAmount) ?? 0m;
        return new CaptureDetails(
            captured.Id,
            captured.Status?.Value ?? string.Empty,
            capturedAmount,
            ParseAmount(breakdown?.PaypalFee),
            ParseAmount(breakdown?.NetAmount),
            captured.Amount?.CurrencyCode ?? breakdown?.GrossAmount?.CurrencyCode ?? currency);
    }

    private static RefundDetails MapRefund(Refund refund, string currency)
    {
        if (string.IsNullOrEmpty(refund.Id))
        {
            throw new PaymentException("PayPal did not return a refund id.", 502);
        }

        return new RefundDetails(
            refund.Id,
            refund.Status?.Value ?? string.Empty,
            ParseAmount(refund.Amount) ?? 0m,
            refund.Amount?.CurrencyCode ?? currency,
            ParseAmount(refund.SellerPayableBreakdown?.TotalRefundedAmount));
    }

    private static PaymentException MapTypedError(CreateOrderError error, string action)
    {
        if (error.TryGetError(out Error body))
        {
            return FromError(body, 400, action);
        }

        if (error.TryGetRawError(out RawError raw))
        {
            return FromRaw(raw, action);
        }

        return new PaymentException($"PayPal {action} failed.", 502);
    }

    private static PaymentException MapTypedError(AuthorizeOrderError error, string action)
    {
        if (error.TryGetError(out Error body))
        {
            return FromError(body, 400, action);
        }

        if (error.TryGetRawError(out RawError raw))
        {
            return FromRaw(raw, action);
        }

        return new PaymentException($"PayPal {action} failed.", 502);
    }

    private static PaymentException MapGetAuthorizedError(SdkException<GetAuthorizedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out Error body))
        {
            return FromError(body, 404, "load authorization");
        }

        if (ex.Error.TryGetNoContent(out RawError noContent))
        {
            return FromRaw(noContent, "load authorization");
        }

        if (ex.Error.TryGetRawError(out RawError raw))
        {
            return FromRaw(raw, "load authorization");
        }

        return new PaymentException("PayPal load authorization failed.", 502);
    }

    private static PaymentException MapReauthorizeError(SdkException<ReauthorizePaymentError> ex)
    {
        if (ex.Error.TryGetError(out Error body))
        {
            var status = 422;
            return FromError(body, status, "reauthorize");
        }

        if (ex.Error.TryGetNoContent(out RawError noContent))
        {
            return FromRaw(noContent, "reauthorize");
        }

        if (ex.Error.TryGetRawError(out RawError raw))
        {
            return FromRaw(raw, "reauthorize");
        }

        return new PaymentException("PayPal reauthorize failed.", 502);
    }

    private static PaymentException MapCaptureError(SdkException<CaptureAuthorizedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out Error body))
        {
            return FromError(body, 409, "capture");
        }

        if (ex.Error.TryGetNoContent(out RawError noContent))
        {
            return FromRaw(noContent, "capture");
        }

        if (ex.Error.TryGetRawError(out RawError raw))
        {
            return FromRaw(raw, "capture");
        }

        return new PaymentException("PayPal capture failed.", 502);
    }

    private static PaymentException MapVoidError(SdkException<VoidPaymentError> ex)
    {
        if (ex.Error.TryGetError(out Error body))
        {
            return FromError(body, 409, "void");
        }

        if (ex.Error.TryGetNoContent(out RawError noContent))
        {
            return FromRaw(noContent, "void");
        }

        if (ex.Error.TryGetRawError(out RawError raw))
        {
            return FromRaw(raw, "void");
        }

        return new PaymentException("PayPal void failed.", 502);
    }

    private static PaymentException MapRefundError(SdkException<RefundCapturedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out Error body))
        {
            return FromError(body, 409, "refund");
        }

        if (ex.Error.TryGetNoContent(out RawError noContent))
        {
            return FromRaw(noContent, "refund");
        }

        if (ex.Error.TryGetRawError(out RawError raw))
        {
            return FromRaw(raw, "refund");
        }

        return new PaymentException("PayPal refund failed.", 502);
    }

    private static PaymentException MapVaultError(CreatePaymentTokenError error, string action)
    {
        if (error.TryGetError1(out Error1 body))
        {
            return FromError1(body, 400, action);
        }

        if (error.TryGetRawError(out RawError raw))
        {
            return FromRaw(raw, action);
        }

        return new PaymentException($"PayPal {action} failed.", 502);
    }

    private static PaymentException MapVaultError(DeletePaymentTokenError error, string action)
    {
        if (error.TryGetError1(out Error1 body))
        {
            return FromError1(body, 400, action);
        }

        if (error.TryGetRawError(out RawError raw))
        {
            return FromRaw(raw, action);
        }

        return new PaymentException($"PayPal {action} failed.", 502);
    }

    private static PaymentException FromError(Error error, int fallbackStatus, string action)
    {
        var details = error.Details is { Count: > 0 }
            ? string.Join("; ", error.Details.Select(d => string.IsNullOrEmpty(d.Description) ? d.Issue : $"{d.Issue}: {d.Description}"))
            : error.Message;
        var message = string.IsNullOrWhiteSpace(details)
            ? $"PayPal {action} failed."
            : $"PayPal {action} failed: {details}";
        return new PaymentException(message, MapProviderStatus(fallbackStatus));
    }

    private static PaymentException FromError1(Error1 error, int fallbackStatus, string action)
    {
        var details = error.Details is { Count: > 0 }
            ? string.Join("; ", error.Details.Select(d => string.IsNullOrEmpty(d.Description) ? d.Issue : $"{d.Issue}: {d.Description}"))
            : error.Message;
        var message = string.IsNullOrWhiteSpace(details)
            ? $"PayPal {action} failed."
            : $"PayPal {action} failed: {details}";
        return new PaymentException(message, MapProviderStatus(fallbackStatus));
    }

    private static PaymentException FromRaw(RawError raw, string action)
    {
        var status = (int)raw.StatusCode;
        string? body = null;
        try
        {
            body = raw.ReadAsString();
        }
        catch (Exception)
        {
            // Body is optional; status is enough for the caller.
        }

        var message = string.IsNullOrWhiteSpace(body)
            ? $"PayPal {action} failed."
            : $"PayPal {action} failed.";
        return new PaymentException(message, MapProviderStatus(status));
    }

    private static int MapProviderStatus(int status) =>
        status switch
        {
            400 or 401 or 403 or 404 or 409 or 422 => status == 401 ? 502 : status,
            >= 400 and < 500 => status,
            _ => 502
        };

    private static string FormatAmount(decimal amount) =>
        amount.ToString("F2", CultureInfo.InvariantCulture);

    private static string FormatSearchDate(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static decimal? ParseAmount(Money? money)
    {
        if (money is null || string.IsNullOrWhiteSpace(money.Value))
        {
            return null;
        }

        return decimal.Parse(money.Value, CultureInfo.InvariantCulture);
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        PayPalCallContext.LastStatusCode = null;
        try
        {
            return await call(cts.Token);
        }
        catch (PaymentException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            var status = PayPalCallContext.LastStatusCode;
            if (status is >= 400 and < 500)
            {
                throw new PaymentException("PayPal rejected the request.", status.Value, ex);
            }

            throw new PaymentException("The payment provider returned a response that could not be processed.", 502, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentException("PayPal is unreachable.", 503, ex);
        }
    }

    private Task Bounded(Func<CancellationToken, Task> call, CancellationToken cancellationToken) =>
        Bounded(async ct =>
        {
            await call(ct);
            return true;
        }, cancellationToken);
}
