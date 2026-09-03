using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using SdkCardRequest = PayPalServerSdk.Models.CardRequest;
using SdkOrderStatus = PayPalServerSdk.Models.Enums.OrderStatus;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PayPalGateway : IPayPalGateway
{
    private const string Representation = "return=representation";
    private readonly PayPalServerSdkClient _client;

    public PayPalGateway(PayPalServerSdkClient client) => _client = client;

    public async Task<AuthorizationResult> AuthorizeAsync(int orderId, string paymentReference, decimal total,
        string currency, object paymentSource, CancellationToken cancellationToken)
    {
        return await Bounded(async ct =>
        {
            PayPalServerSdk.Models.Order providerOrder;
            try
            {
                providerOrder = await _client.Orders.CreateOrder(
                    payPalMockResponse: null,
                    payPalRequestId: RequestId(paymentReference, "create"),
                    payPalPartnerAttributionId: null,
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: new OrderRequest
                    {
                        Intent = CheckoutPaymentIntent.Authorize,
                        PurchaseUnits =
                        [
                            new PurchaseUnitRequest
                            {
                                ReferenceId = orderId.ToString(CultureInfo.InvariantCulture),
                                InvoiceId = InvoiceId(paymentReference),
                                CustomId = orderId.ToString(CultureInfo.InvariantCulture),
                                Amount = new AmountWithBreakdown
                                {
                                    CurrencyCode = currency,
                                    Value = Money(total)
                                }
                            }
                        ]
                    },
                    prefer: Representation,
                    ct: ct);
            }
            catch (SdkException<CreateOrderError> ex)
            {
                throw Convert(ex.Error, ex);
            }

            var providerOrderId = Required(providerOrder.Id, "PayPal did not return an order ID.");
            if (providerOrder.Status == SdkOrderStatus.PayerActionRequired)
            {
                throw new PayPalPayerActionRequiredException(providerOrderId);
            }

            var card = paymentSource switch
            {
                CardSource oneOff => ToSdkCard(oneOff.Card),
                SavedCardSource saved => new SdkCardRequest
                {
                    VaultId = saved.ProviderTokenId,
                    StoredCredential = new CardStoredCredential
                    {
                        PaymentInitiator = PaymentInitiator.Customer,
                        PaymentType = StoredPaymentSourcePaymentType.OneTime,
                        Usage = StoredPaymentSourceUsageType.Subsequent
                    }
                },
                _ => throw new InvalidOperationException("Unsupported PayPal payment source.")
            };

            OrderAuthorizeResponse authorizationResponse;
            try
            {
                authorizationResponse = await _client.Orders.AuthorizeOrder(
                    id: providerOrderId,
                    payPalMockResponse: null,
                    payPalRequestId: RequestId(paymentReference, "authorize"),
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: new OrderAuthorizeRequest
                    {
                        PaymentSource = new OrderAuthorizeRequestPaymentSource { Card = card }
                    },
                    prefer: Representation,
                    ct: ct);
            }
            catch (SdkException<AuthorizeOrderError> ex)
            {
                throw Convert(ex.Error, ex);
            }

            if (authorizationResponse.Status == SdkOrderStatus.PayerActionRequired)
            {
                throw new PayPalPayerActionRequiredException(providerOrderId);
            }

            var authorization = authorizationResponse.PurchaseUnits?
                .SelectMany(x => x.Payments?.Authorizations ?? [])
                .FirstOrDefault() ?? throw new PayPalProviderException("PayPal did not return an authorization.");
            var amount = ParseMoney(authorization.Amount, "authorization");
            EnsureAmount(total, currency, authorization.Amount, amount, "authorization");
            var status = authorization.Status?.Value ?? "UNKNOWN";
            if (status is not "CREATED" and not "PENDING")
                throw new PayPalProviderException($"PayPal authorization finished in unexpected status {status}.");

            return new AuthorizationResult(
                providerOrderId,
                Required(authorization.Id, "PayPal did not return an authorization ID."),
                status,
                amount,
                ParseDate(authorization.ExpirationTime));
        }, cancellationToken);
    }

    public async Task<ReauthorizationResult> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken)
    {
        return await Bounded(async ct =>
        {
            try
            {
                var result = await _client.Payments.GetAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    ct: ct);
                return ToReauthorization(result);
            }
            catch (SdkException<GetAuthorizedPaymentError> ex)
            {
                throw Convert(ex.Error, ex);
            }
        }, cancellationToken);
    }

    public async Task<ReauthorizationResult> ReauthorizeAsync(int orderId, string paymentReference,
        string authorizationId, decimal total, string currency, CancellationToken cancellationToken)
    {
        return await Bounded(async ct =>
        {
            try
            {
                var result = await _client.Payments.ReauthorizePayment(
                    authorizationId: authorizationId,
                    payPalRequestId: RequestId(paymentReference, "reauthorize"),
                    payPalAuthAssertion: null,
                    body: new ReauthorizeRequest { Amount = SdkMoney(total, currency) },
                    prefer: Representation,
                    ct: ct);
                var mapped = ToReauthorization(result);
                EnsureAmount(total, currency, result.Amount, mapped.Amount, "reauthorization");
                if (mapped.Status != "CREATED")
                    throw new PayPalProviderException($"PayPal reauthorization finished in status {mapped.Status}.");
                return mapped;
            }
            catch (SdkException<ReauthorizePaymentError> ex)
            {
                throw Convert(ex.Error, ex);
            }
        }, cancellationToken);
    }

    public async Task<CaptureResult> CaptureAsync(int orderId, string paymentReference, string authorizationId,
        decimal total, string currency, CancellationToken cancellationToken)
    {
        return await Bounded(async ct =>
        {
            try
            {
                var result = await _client.Payments.CaptureAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalRequestId: RequestId(paymentReference, "capture"),
                    payPalAuthAssertion: null,
                    body: new CaptureRequest
                    {
                        Amount = SdkMoney(total, currency),
                        InvoiceId = InvoiceId(paymentReference),
                        FinalCapture = true
                    },
                    prefer: Representation,
                    ct: ct);
                var amount = ParseMoney(result.Amount, "capture");
                EnsureAmount(total, currency, result.Amount, amount, "capture");
                return new CaptureResult(
                    Required(result.Id, "PayPal did not return a capture ID."),
                    result.Status?.Value ?? "UNKNOWN",
                    amount,
                    ParseOptionalMoney(result.SellerReceivableBreakdown?.PaypalFee),
                    ParseOptionalMoney(result.SellerReceivableBreakdown?.NetAmount));
            }
            catch (SdkException<CaptureAuthorizedPaymentError> ex)
            {
                throw Convert(ex.Error, ex);
            }
        }, cancellationToken);
    }

    public async Task<VoidResult> VoidAsync(int orderId, string paymentReference, string authorizationId,
        CancellationToken cancellationToken)
    {
        return await Bounded(async ct =>
        {
            try
            {
                var result = await _client.Payments.VoidPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    payPalRequestId: RequestId(paymentReference, "void"),
                    prefer: Representation,
                    ct: ct);
                var status = result.Status?.Value ?? "UNKNOWN";
                if (status != "VOIDED")
                    throw new PayPalProviderException($"PayPal void finished in status {status}.");
                return new VoidResult(status);
            }
            catch (SdkException<VoidPaymentError> ex)
            {
                throw Convert(ex.Error, ex);
            }
        }, cancellationToken);
    }

    public async Task<ProviderRefundResult> RefundAsync(int orderId, string paymentReference, string captureId,
        decimal amount, string currency, string requestId, CancellationToken cancellationToken)
    {
        return await Bounded(async ct =>
        {
            try
            {
                var result = await _client.Payments.RefundCapturedPayment(
                    captureId: captureId,
                    payPalMockResponse: null,
                    payPalRequestId: requestId,
                    payPalAuthAssertion: null,
                    body: new PayPalServerSdk.Models.RefundRequest
                    {
                        Amount = SdkMoney(amount, currency),
                        CustomId = orderId.ToString(CultureInfo.InvariantCulture),
                        InvoiceId = InvoiceId(paymentReference)
                    },
                    prefer: Representation,
                    ct: ct);
                var refunded = ParseMoney(result.Amount, "refund");
                EnsureAmount(amount, currency, result.Amount, refunded, "refund");
                return new ProviderRefundResult(
                    Required(result.Id, "PayPal did not return a refund ID."),
                    result.Status?.Value ?? "UNKNOWN",
                    refunded);
            }
            catch (SdkException<RefundCapturedPaymentError> ex)
            {
                throw Convert(ex.Error, ex);
            }
        }, cancellationToken);
    }

    public async Task<ProviderRefundResult> GetRefundAsync(string refundId, decimal expectedAmount,
        string currency, CancellationToken cancellationToken)
    {
        return await Bounded(async ct =>
        {
            try
            {
                var result = await _client.Payments.GetRefund(
                    refundId: refundId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    ct: ct);
                var amount = ParseMoney(result.Amount, "refund");
                EnsureAmount(expectedAmount, currency, result.Amount, amount, "refund");
                return new ProviderRefundResult(
                    Required(result.Id, "PayPal did not return a refund ID."),
                    result.Status?.Value ?? "UNKNOWN",
                    amount);
            }
            catch (SdkException<GetRefundError> ex)
            {
                throw Convert(ex.Error, ex);
            }
        }, cancellationToken);
    }

    public async Task<VaultResult> SaveCardAsync(string paymentReference, string buyerId, CardRequest card,
        CancellationToken cancellationToken)
    {
        return await Bounded(async ct =>
        {
            try
            {
                var result = await _client.Vault.CreatePaymentToken(
                    payPalRequestId: RequestId(paymentReference, "vault"),
                    body: new PaymentTokenRequest
                    {
                        Customer = new Customer { MerchantCustomerId = StableCustomerId(buyerId) },
                        PaymentSource = new PaymentTokenRequestPaymentSource
                        {
                            Card = new PaymentTokenRequestCard
                            {
                                Name = card.Name.Trim(),
                                Number = card.Number,
                                Expiry = card.Expiry,
                                SecurityCode = card.SecurityCode,
                                BillingAddress = ToSdkAddress(card.BillingAddress)
                            }
                        }
                    },
                    ct: ct);
                var saved = result.PaymentSource?.Card ??
                    throw new PayPalProviderException("PayPal did not return the saved card details.");
                return new VaultResult(
                    Required(result.Id, "PayPal did not return a payment token ID."),
                    result.Customer?.Id,
                    saved.Brand?.Value ?? "UNKNOWN",
                    Required(saved.LastDigits, "PayPal did not return masked card digits."),
                    saved.Expiry);
            }
            catch (SdkException<CreatePaymentTokenError> ex)
            {
                throw Convert(ex.Error, ex);
            }
        }, cancellationToken);
    }

    public async Task DeleteCardAsync(string providerTokenId, CancellationToken cancellationToken)
    {
        await Bounded(async ct =>
        {
            try
            {
                await _client.Vault.DeletePaymentToken(id: providerTokenId, ct: ct);
                return true;
            }
            catch (SdkException<DeletePaymentTokenError> ex)
            {
                throw Convert(ex.Error, ex);
            }
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, string currency, CancellationToken cancellationToken)
    {
        var results = new List<ProviderTransaction>();
        var requestedFrom = from.ToUniversalTime();
        var requestedTo = to.ToUniversalTime();
        var cursor = WholeSecondFloor(from.ToUniversalTime());
        var rangeEnd = WholeSecondCeiling(to.ToUniversalTime());

        while (cursor <= rangeEnd)
        {
            var chunkEnd = cursor.AddDays(31).AddSeconds(-1);
            if (chunkEnd > rangeEnd) chunkEnd = rangeEnd;

            const int pageSize = 100;
            const int maxPagesPerChunk = 10000;
            var page = 1;
            while (true)
            {
                var response = await Bounded(async ct =>
                {
                    try
                    {
                        return await _client.TransactionSearch.SearchTransactions(
                            startDate: FormatDate(cursor),
                            endDate: FormatDate(chunkEnd),
                            transactionId: null,
                            transactionType: null,
                            transactionStatus: null,
                            transactionAmount: null,
                            transactionCurrency: currency,
                            paymentInstrumentType: null,
                            storeId: null,
                            terminalId: null,
                            fields: "transaction_info",
                            balanceAffectingRecordsOnly: "N",
                            pageSize: pageSize,
                            page: page,
                            ct: ct);
                    }
                    catch (SdkException<RawError> ex)
                    {
                        throw new PayPalProviderException(
                            $"PayPal transaction search failed with HTTP {(int)ex.Error.StatusCode}.",
                            code: ((int)ex.Error.StatusCode).ToString(CultureInfo.InvariantCulture),
                            innerException: ex);
                    }
                }, cancellationToken);

                foreach (var item in response.TransactionDetails ?? [])
                {
                    var info = item.TransactionInfo;
                    if (info is null) continue;
                    results.Add(new ProviderTransaction(
                        info.TransactionId,
                        info.PaypalReferenceId,
                        info.TransactionEventCode,
                        ParseDate(info.TransactionInitiationDate),
                        ParseOptionalMoney(info.TransactionAmount),
                        ParseOptionalMoney(info.FeeAmount),
                        info.TransactionAmount?.CurrencyCode,
                        info.TransactionStatus,
                        info.InvoiceId));
                }

                var returnedCount = response.TransactionDetails?.Count ?? 0;
                if ((response.TotalPages is int totalPages && page >= totalPages) ||
                    (response.TotalPages is null && returnedCount < pageSize))
                    break;
                if (page >= maxPagesPerChunk)
                {
                    throw new PayPalProviderException("PayPal reconciliation exceeded its safety page limit; no partial report was returned.");
                }
                page++;
            }

            if (chunkEnd == rangeEnd) break;
            cursor = chunkEnd.AddSeconds(1);
        }

        return results.Where(x => x.InitiatedAt is null ||
                                  x.InitiatedAt >= requestedFrom && x.InitiatedAt <= requestedTo).ToList();
    }

    private static SdkCardRequest ToSdkCard(CardRequest card) => new()
    {
        Name = card.Name.Trim(),
        Number = card.Number,
        Expiry = card.Expiry,
        SecurityCode = card.SecurityCode,
        BillingAddress = ToSdkAddress(card.BillingAddress)
    };

    private static Address ToSdkAddress(PostalAddressRequest address) => new()
    {
        AddressLine1 = address.Street.Trim(),
        AddressLine2 = address.AddressLine2?.Trim(),
        AdminArea2 = address.City.Trim(),
        AdminArea1 = address.State?.Trim(),
        PostalCode = address.ZipCode.Trim(),
        CountryCode = address.Country.Trim().ToUpperInvariant()
    };

    private static Money SdkMoney(decimal amount, string currency) => new()
    {
        CurrencyCode = currency,
        Value = Money(amount)
    };

    private static ReauthorizationResult ToReauthorization(PaymentAuthorization result) => new(
        Required(result.Id, "PayPal did not return an authorization ID."),
        result.Status?.Value ?? "UNKNOWN",
        ParseMoney(result.Amount, "authorization"),
        Required(result.Amount?.CurrencyCode, "PayPal did not return an authorization currency."),
        ParseDate(result.ExpirationTime));

    private static PayPalProviderException Convert(ApiError error, Exception inner)
    {
        if (error.TryGetRawError(out var raw))
        {
            return new PayPalProviderException(
                $"PayPal returned HTTP {(int)raw.StatusCode}.",
                ((int)raw.StatusCode).ToString(CultureInfo.InvariantCulture),
                innerException: inner);
        }

        return new PayPalProviderException("PayPal returned an unrecognized error response.", innerException: inner);
    }

    private static PayPalProviderException Convert(CreateOrderError error, Exception inner) =>
        ConvertTyped(error.TryGetError(out var value) ? value : null, error, inner);
    private static PayPalProviderException Convert(AuthorizeOrderError error, Exception inner) =>
        ConvertTyped(error.TryGetError(out var value) ? value : null, error, inner);
    private static PayPalProviderException Convert(GetAuthorizedPaymentError error, Exception inner) =>
        ConvertPaymentError(error.TryGetError(out var value) ? value : null,
            error.TryGetNoContent(out var noContent) ? noContent : null, error, inner);
    private static PayPalProviderException Convert(ReauthorizePaymentError error, Exception inner) =>
        ConvertPaymentError(error.TryGetError(out var value) ? value : null,
            error.TryGetNoContent(out var noContent) ? noContent : null, error, inner);
    private static PayPalProviderException Convert(CaptureAuthorizedPaymentError error, Exception inner) =>
        ConvertPaymentError(error.TryGetError(out var value) ? value : null,
            error.TryGetNoContent(out var noContent) ? noContent : null, error, inner);
    private static PayPalProviderException Convert(VoidPaymentError error, Exception inner) =>
        ConvertPaymentError(error.TryGetError(out var value) ? value : null,
            error.TryGetNoContent(out var noContent) ? noContent : null, error, inner);
    private static PayPalProviderException Convert(RefundCapturedPaymentError error, Exception inner) =>
        ConvertPaymentError(error.TryGetError(out var value) ? value : null,
            error.TryGetNoContent(out var noContent) ? noContent : null, error, inner);
    private static PayPalProviderException Convert(GetRefundError error, Exception inner) =>
        ConvertPaymentError(error.TryGetError(out var value) ? value : null,
            error.TryGetNoContent(out var noContent) ? noContent : null, error, inner);
    private static PayPalProviderException Convert(CreatePaymentTokenError error, Exception inner) =>
        ConvertTyped(error.TryGetError(out var value) ? value : null, error, inner);
    private static PayPalProviderException Convert(DeletePaymentTokenError error, Exception inner) =>
        ConvertTyped(error.TryGetError(out var value) ? value : null, error, inner);

    private static PayPalProviderException ConvertPaymentError(Error? typed, RawError? statusRaw,
        ApiError error, Exception inner)
    {
        if (typed is not null) return FromTyped(typed, inner);
        if (statusRaw is not null)
        {
            return new PayPalProviderException($"PayPal returned HTTP {(int)statusRaw.StatusCode}.",
                ((int)statusRaw.StatusCode).ToString(CultureInfo.InvariantCulture), innerException: inner);
        }
        return Convert(error, inner);
    }

    private static PayPalProviderException ConvertTyped(Error? typed, ApiError error, Exception inner) =>
        typed is not null ? FromTyped(typed, inner) : Convert(error, inner);

    private static PayPalProviderException FromTyped(Error error, Exception inner)
    {
        var detail = error.Details?.FirstOrDefault();
        var message = detail?.Description ?? error.Message;
        if (!string.IsNullOrWhiteSpace(detail?.Issue)) message = $"{detail.Issue}: {message}";
        return new PayPalProviderException(message, detail?.Issue ?? error.Name, error.DebugId, inner);
    }

    private static async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken callerToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        try
        {
            return await call(timeout.Token);
        }
        catch (PayPalProviderException) { throw; }
        catch (PayPalPayerActionRequiredException) { throw; }
        catch (JsonException ex)
        {
            throw new PayPalProviderException("PayPal returned a response that could not be processed.", innerException: ex);
        }
        catch (HttpRequestException ex)
        {
            throw new PayPalProviderException("PayPal is unreachable; the operation outcome may be unknown.", innerException: ex);
        }
        catch (TaskCanceledException ex) when (!callerToken.IsCancellationRequested)
        {
            throw new PayPalProviderException("The PayPal operation timed out; its outcome may be unknown.", innerException: ex);
        }
    }

    private static string Money(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);
    private static string InvoiceId(string paymentReference) => $"eshop-{paymentReference}";
    private static string RequestId(string paymentReference, string operation) =>
        $"eshop-{paymentReference}-{operation}";
    private static string StableCustomerId(string buyerId) => System.Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(buyerId)))[..32];
    private static string FormatDate(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    private static DateTimeOffset WholeSecondFloor(DateTimeOffset value) =>
        value.AddTicks(-(value.Ticks % TimeSpan.TicksPerSecond));
    private static DateTimeOffset WholeSecondCeiling(DateTimeOffset value)
    {
        var floor = WholeSecondFloor(value);
        return floor == value ? value : floor.AddSeconds(1);
    }

    private static string Required(string? value, string message) =>
        !string.IsNullOrWhiteSpace(value) ? value : throw new PayPalProviderException(message);

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;

    private static decimal ParseMoney(Money? money, string kind)
    {
        if (money is null || !decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture,
                out var amount))
        {
            throw new PayPalProviderException($"PayPal returned an invalid {kind} amount.");
        }
        return amount;
    }

    private static decimal? ParseOptionalMoney(Money? money) => money is null ? null : ParseMoney(money, "transaction");

    private static void EnsureAmount(decimal expected, string currency, Money? providerMoney,
        decimal actual, string kind)
    {
        if (actual != expected || providerMoney?.CurrencyCode != currency)
        {
            throw new PayPalProviderException($"PayPal {kind} amount did not match the order total.");
        }
    }
}
