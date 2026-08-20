using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
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
using Address = PayPalServerSdk.Models.Address;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public sealed class PayPalGateway : IPayPalGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private const string PreferRepresentation = "return=representation";

    private readonly PayPalServerSdkClient _client;

    public PayPalGateway(PayPalServerSdkClient client)
    {
        _client = client;
    }

    public Task<PayPalAuthorizationResult> AuthorizeCardAsync(
        int orderId,
        string invoiceId,
        string amountValue,
        string currency,
        PayPalCardInput card,
        string payPalRequestId,
        CancellationToken cancellationToken)
    {
        var cardRequest = new CardRequest
        {
            Name = card.Name,
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            BillingAddress = ToPayPalAddress(card.BillingAddress)
        };
        return AuthorizeAsync(orderId, invoiceId, amountValue, currency, cardRequest, payPalRequestId, cancellationToken);
    }

    public Task<PayPalAuthorizationResult> AuthorizeSavedCardAsync(
        int orderId,
        string invoiceId,
        string amountValue,
        string currency,
        string vaultId,
        string payPalRequestId,
        CancellationToken cancellationToken)
    {
        var cardRequest = new CardRequest
        {
            VaultId = vaultId,
            StoredCredential = new CardStoredCredential
            {
                PaymentInitiator = PaymentInitiator.Customer,
                PaymentType = StoredPaymentSourcePaymentType.Unscheduled,
                Usage = StoredPaymentSourceUsageType.Subsequent
            }
        };
        return AuthorizeAsync(orderId, invoiceId, amountValue, currency, cardRequest, payPalRequestId, cancellationToken);
    }

    public Task<PayPalAuthorizationSnapshot> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken) =>
        Invoke(async ct =>
        {
            try
            {
                var auth = await _client.Payments.GetAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    requestOptions: null,
                    ct: ct);
                return ToSnapshot(auth);
            }
            catch (SdkException<GetAuthorizedPaymentError> ex)
            {
                throw MapPaymentsError("GetAuthorizedPayment", ex.Error.TryGetError, ex.Error.TryGetNoContent, ex.Error.TryGetRawError);
            }
        }, cancellationToken);

    public Task<PayPalAuthorizationSnapshot> ReauthorizeAsync(
        string authorizationId,
        string amountValue,
        string currency,
        string payPalRequestId,
        CancellationToken cancellationToken) =>
        Invoke(async ct =>
        {
            try
            {
                var auth = await _client.Payments.ReauthorizePayment(
                    authorizationId: authorizationId,
                    payPalRequestId: payPalRequestId,
                    payPalAuthAssertion: null,
                    body: new ReauthorizeRequest
                    {
                        Amount = new Money { CurrencyCode = currency, Value = amountValue }
                    },
                    prefer: PreferRepresentation,
                    requestOptions: null,
                    ct: ct);
                return ToSnapshot(auth);
            }
            catch (SdkException<ReauthorizePaymentError> ex)
            {
                throw MapPaymentsError("ReauthorizePayment", ex.Error.TryGetError, ex.Error.TryGetNoContent, ex.Error.TryGetRawError);
            }
        }, cancellationToken);

    public Task<PayPalCaptureResult> CaptureAsync(
        string authorizationId,
        int orderId,
        string payPalRequestId,
        CancellationToken cancellationToken) =>
        Invoke(async ct =>
        {
            try
            {
                var capture = await _client.Payments.CaptureAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalRequestId: payPalRequestId,
                    payPalAuthAssertion: null,
                    body: new CaptureRequest
                    {
                        FinalCapture = true
                    },
                    prefer: PreferRepresentation,
                    requestOptions: null,
                    ct: ct);
                return ToCapture(capture);
            }
            catch (SdkException<CaptureAuthorizedPaymentError> ex)
            {
                throw MapPaymentsError("CaptureAuthorizedPayment", ex.Error.TryGetError, ex.Error.TryGetNoContent, ex.Error.TryGetRawError);
            }
        }, cancellationToken);

    public Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken) =>
        Invoke(async ct =>
        {
            try
            {
                var capture = await _client.Payments.GetCapturedPayment(
                    captureId: captureId,
                    payPalMockResponse: null,
                    requestOptions: null,
                    ct: ct);
                return ToCapture(capture);
            }
            catch (SdkException<GetCapturedPaymentError> ex)
            {
                throw MapPaymentsError("GetCapturedPayment", ex.Error.TryGetError, ex.Error.TryGetNoContent, ex.Error.TryGetRawError);
            }
        }, cancellationToken);

    public Task<PayPalAuthorizationSnapshot> VoidAsync(
        string authorizationId,
        string payPalRequestId,
        CancellationToken cancellationToken) =>
        Invoke(async ct =>
        {
            try
            {
                var auth = await _client.Payments.VoidPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    payPalRequestId: payPalRequestId,
                    prefer: PreferRepresentation,
                    requestOptions: null,
                    ct: ct);
                return ToSnapshot(auth);
            }
            catch (SdkException<VoidPaymentError> ex)
            {
                throw MapPaymentsError("VoidPayment", ex.Error.TryGetError, ex.Error.TryGetNoContent, ex.Error.TryGetRawError);
            }
        }, cancellationToken);

    public Task<PayPalRefundResult> RefundAsync(
        string captureId,
        string amountValue,
        string currency,
        string payPalRequestId,
        bool fullRefund,
        CancellationToken cancellationToken) =>
        Invoke(async ct =>
        {
            try
            {
                RefundRequest? body = fullRefund
                    ? null
                    : new RefundRequest
                    {
                        Amount = new Money { CurrencyCode = currency, Value = amountValue }
                    };

                var refund = await _client.Payments.RefundCapturedPayment(
                    captureId: captureId,
                    payPalMockResponse: null,
                    payPalRequestId: payPalRequestId,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: PreferRepresentation,
                    requestOptions: null,
                    ct: ct);

                if (string.IsNullOrEmpty(refund.Id))
                    throw new CheckoutException(502, "PayPal did not return a refund id.");

                return new PayPalRefundResult(
                    refund.Id,
                    StatusWire(refund.Status),
                    ToMoneyDto(refund.Amount) ?? new PayPalMoneyDto(currency, amountValue),
                    refund.SellerPayableBreakdown?.TotalRefundedAmount == null
                        ? null
                        : ToMoneyDto(refund.SellerPayableBreakdown.TotalRefundedAmount));
            }
            catch (SdkException<RefundCapturedPaymentError> ex)
            {
                throw MapPaymentsError("RefundCapturedPayment", ex.Error.TryGetError, ex.Error.TryGetNoContent, ex.Error.TryGetRawError);
            }
        }, cancellationToken);

    public Task<PayPalVaultedCard> VaultCardAsync(
        string merchantCustomerId,
        PayPalCardInput card,
        string payPalRequestId,
        CancellationToken cancellationToken) =>
        Invoke(async ct =>
        {
            try
            {
                var response = await _client.Vault.CreatePaymentToken(
                    payPalRequestId: payPalRequestId,
                    body: new PaymentTokenRequest
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
                    },
                    requestOptions: null,
                    ct: ct);

                if (string.IsNullOrEmpty(response.Id))
                    throw new CheckoutException(502, "PayPal did not return a payment token id.");

                var cardEntity = response.PaymentSource?.Card;
                return new PayPalVaultedCard(
                    response.Id,
                    response.Customer?.Id,
                    cardEntity?.LastDigits,
                    cardEntity?.Brand == null ? null : cardEntity.Brand.Value,
                    cardEntity?.Expiry,
                    cardEntity?.Name,
                    cardEntity?.Type == null ? null : cardEntity.Type.Value);
            }
            catch (SdkException<CreatePaymentTokenError> ex)
            {
                throw MapVaultError("CreatePaymentToken", ex.Error.TryGetError1, ex.Error.TryGetRawError);
            }
        }, cancellationToken);

    public Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken cancellationToken) =>
        Invoke(async ct =>
        {
            try
            {
                await _client.Vault.DeletePaymentToken(
                    id: paymentTokenId,
                    requestOptions: null,
                    ct: ct);
                return 0;
            }
            catch (SdkException<DeletePaymentTokenError> ex)
            {
                throw MapVaultError("DeletePaymentToken", ex.Error.TryGetError1, ex.Error.TryGetRawError);
            }
        }, cancellationToken);

    public Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(
        string startDate,
        string endDate,
        CancellationToken cancellationToken) =>
        Invoke(async ct =>
        {
            var collected = new List<PayPalTransactionRecord>();
            var page = 1;
            int? totalPages = null;
            const int pageSize = 100;

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
                        balanceAffectingRecordsOnly: "Y",
                        pageSize: pageSize,
                        page: page,
                        requestOptions: null,
                        ct: ct);

                    var details = response.TransactionDetails;
                    if (details != null)
                    {
                        foreach (var detail in details)
                        {
                            var info = detail.TransactionInfo;
                            if (info == null)
                                continue;
                            collected.Add(new PayPalTransactionRecord(
                                info.TransactionId,
                                info.PaypalReferenceId,
                                info.TransactionInitiationDate,
                                info.TransactionUpdatedDate,
                                ToMoneyDto(info.TransactionAmount),
                                ToMoneyDto(info.FeeAmount),
                                info.TransactionStatus,
                                info.InvoiceId,
                                info.CustomField));
                        }
                    }

                    totalPages = response.TotalPages;
                    var pageCount = details?.Count ?? 0;
                    if (totalPages.HasValue)
                    {
                        if (page >= totalPages.Value)
                            break;
                    }
                    else if (pageCount < pageSize)
                    {
                        break;
                    }

                    page++;
                    if (page > 1000)
                        break;
                }
            }
            catch (SdkException<RawError> ex)
            {
                throw new CheckoutException(
                    MapHttpStatus((int)ex.Error.StatusCode),
                    $"SearchTransactions failed: {ex.Error.ReadAsString()}");
            }

            return (IReadOnlyList<PayPalTransactionRecord>)collected;
        }, cancellationToken);

    private Task<PayPalAuthorizationResult> AuthorizeAsync(
        int orderId,
        string invoiceId,
        string amountValue,
        string currency,
        CardRequest cardRequest,
        string payPalRequestId,
        CancellationToken cancellationToken) =>
        Invoke(async ct =>
        {
            var customId = orderId.ToString(CultureInfo.InvariantCulture);
            PayPalServerSdk.Models.Order created;
            try
            {
                created = await _client.Orders.CreateOrder(
                    payPalMockResponse: null,
                    payPalRequestId: payPalRequestId,
                    payPalPartnerAttributionId: null,
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: new OrderRequest
                    {
                        Intent = CheckoutPaymentIntent.Authorize,
                        PurchaseUnits = new List<PurchaseUnitRequest>
                        {
                            new()
                            {
                                Amount = new AmountWithBreakdown
                                {
                                    CurrencyCode = currency,
                                    Value = amountValue
                                },
                                CustomId = customId,
                                InvoiceId = invoiceId
                            }
                        }
                    },
                    prefer: PreferRepresentation,
                    requestOptions: null,
                    ct: ct);
            }
            catch (SdkException<CreateOrderError> ex)
            {
                throw MapCreateOrder(ex);
            }

            if (string.IsNullOrEmpty(created.Id))
                throw new CheckoutException(502, "PayPal did not return an order id.");
            if (created.Status == OrderStatus.PayerActionRequired)
                throw PayerActionRequired();

            OrderAuthorizeResponse authorized;
            try
            {
                authorized = await _client.Orders.AuthorizeOrder(
                    id: created.Id,
                    payPalMockResponse: null,
                    payPalRequestId: payPalRequestId,
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: new OrderAuthorizeRequest
                    {
                        PaymentSource = new OrderAuthorizeRequestPaymentSource
                        {
                            Card = cardRequest
                        }
                    },
                    prefer: PreferRepresentation,
                    requestOptions: null,
                    ct: ct);
            }
            catch (SdkException<AuthorizeOrderError> ex)
            {
                throw MapAuthorizeOrder(ex);
            }

            if (authorized.Status == OrderStatus.PayerActionRequired)
                throw PayerActionRequired();
            var auth = FirstAuthorization(authorized.PurchaseUnits);
            return new PayPalAuthorizationResult(
                created.Id,
                auth.Id,
                auth.Status,
                auth.ExpirationTime,
                auth.Amount ?? new PayPalMoneyDto(currency, amountValue));
        }, cancellationToken);

    private static async Task<T> Invoke<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(CallBudget);
            return await call(cts.Token);
        }
        catch (CheckoutException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            var status = PayPalStatusCaptureHandler.LastStatus;
            if (status is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError)
                throw new CheckoutException(400, "PayPal rejected the request.", ex);
            throw new CheckoutException(502, "The provider returned a response that could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
                throw;
            throw new CheckoutException(502, "PayPal is unreachable.", ex);
        }
    }

    private static CheckoutException MapCreateOrder(SdkException<CreateOrderError> ex)
    {
        if (ex.Error.TryGetError(out var error))
            return new CheckoutException(400, FormatError(error));
        if (ex.Error.TryGetRawError(out var raw))
            return FromRaw("CreateOrder", raw);
        return new CheckoutException(502, "CreateOrder failed.");
    }

    private static CheckoutException MapAuthorizeOrder(SdkException<AuthorizeOrderError> ex)
    {
        if (ex.Error.TryGetError(out var error))
            return new CheckoutException(400, FormatError(error));
        if (ex.Error.TryGetRawError(out var raw))
            return FromRaw("AuthorizeOrder", raw);
        return new CheckoutException(502, "AuthorizeOrder failed.");
    }

    private static CheckoutException MapPaymentsError(
        string operation,
        TryGetError tryGetError,
        TryGetRaw tryGetNoContent,
        TryGetRaw tryGetRaw)
    {
        if (tryGetError(out var error))
            return new CheckoutException(400, $"{operation} failed. {FormatError(error)}");
        if (tryGetNoContent(out var noContent))
            return FromRaw(operation, noContent);
        if (tryGetRaw(out var raw))
            return FromRaw(operation, raw);
        return new CheckoutException(502, $"{operation} failed.");
    }

    private static CheckoutException MapVaultError(string operation, TryGetError1 tryGetError1, TryGetRaw tryGetRaw)
    {
        if (tryGetError1(out var error))
            return new CheckoutException(400, $"{operation} failed. {FormatError1(error)}");
        if (tryGetRaw(out var raw))
            return FromRaw(operation, raw);
        return new CheckoutException(502, $"{operation} failed.");
    }

    private delegate bool TryGetError(out Error error);
    private delegate bool TryGetError1(out Error1 error);
    private delegate bool TryGetRaw(out RawError raw);

    private static CheckoutException FromRaw(string operation, RawError raw)
    {
        var status = MapHttpStatus((int)raw.StatusCode);
        return new CheckoutException(status, $"{operation} failed with HTTP {(int)raw.StatusCode}.");
    }

    private static int MapHttpStatus(int status) =>
        status is >= 400 and < 600 ? status : 502;

    private static string FormatError(Error error)
    {
        var details = error.Details == null
            ? string.Empty
            : string.Join("; ", error.Details.Select(d => $"{d.Issue}: {d.Description}"));
        var suffix = string.IsNullOrEmpty(details) ? string.Empty : $" ({details})";
        return $"{error.Name}: {error.Message}{suffix} [debugId={error.DebugId}]";
    }

    private static string FormatError1(Error1 error)
    {
        var details = error.Details == null
            ? string.Empty
            : string.Join("; ", error.Details.Select(d => $"{d.Issue}: {d.Description}"));
        var suffix = string.IsNullOrEmpty(details) ? string.Empty : $" ({details})";
        return $"{error.Name}: {error.Message}{suffix} [debugId={error.DebugId}]";
    }

    private static CheckoutException PayerActionRequired() =>
        new(409, "PayPal required a shopper approval in the browser. This integration does not support that challenge.");

    private static (string Id, string Status, string? ExpirationTime, PayPalMoneyDto? Amount) FirstAuthorization(
        IReadOnlyList<PurchaseUnit>? units)
    {
        var auth = units?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
        if (auth == null || string.IsNullOrEmpty(auth.Id))
            throw new CheckoutException(502, "PayPal did not return an authorization id.");
        return (auth.Id, StatusWire(auth.Status), auth.ExpirationTime, ToMoneyDto(auth.Amount));
    }

    private static PayPalAuthorizationSnapshot ToSnapshot(PaymentAuthorization auth)
    {
        if (string.IsNullOrEmpty(auth.Id))
            throw new CheckoutException(502, "PayPal did not return an authorization id.");
        return new PayPalAuthorizationSnapshot(auth.Id, StatusWire(auth.Status), auth.ExpirationTime, ToMoneyDto(auth.Amount));
    }

    private static PayPalCaptureResult ToCapture(CapturedPayment capture)
    {
        if (string.IsNullOrEmpty(capture.Id))
            throw new CheckoutException(502, "PayPal did not return a capture id.");
        var amount = ToMoneyDto(capture.Amount) ?? new PayPalMoneyDto(string.Empty, "0.00");
        var breakdown = capture.SellerReceivableBreakdown;
        return new PayPalCaptureResult(
            capture.Id,
            StatusWire(capture.Status),
            amount,
            breakdown?.PaypalFee == null ? null : ToMoneyDto(breakdown.PaypalFee),
            breakdown?.NetAmount == null ? null : ToMoneyDto(breakdown.NetAmount));
    }

    private static PayPalMoneyDto? ToMoneyDto(Money? money) =>
        money == null ? null : new PayPalMoneyDto(money.CurrencyCode, money.Value);

    private static string StatusWire(AuthorizationStatus? status) => status?.Value ?? string.Empty;

    private static string StatusWire(CaptureStatus? status) => status?.Value ?? string.Empty;

    private static string StatusWire(RefundStatus? status) => status?.Value ?? string.Empty;

    private static Address? ToPayPalAddress(PayPalBillingAddressInput? input)
    {
        if (input == null || string.IsNullOrWhiteSpace(input.CountryCode))
            return null;
        return new Address
        {
            CountryCode = input.CountryCode,
            AddressLine1 = input.AddressLine1,
            AdminArea1 = input.AdminArea1,
            AdminArea2 = input.AdminArea2,
            PostalCode = input.PostalCode
        };
    }
}
