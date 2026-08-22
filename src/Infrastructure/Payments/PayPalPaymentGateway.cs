using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using PayPalServerSdk.Servers;
using PayPalAddress = PayPalServerSdk.Models.Address;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalPaymentGateway : IPaymentGateway
{
    private const string ReturnRepresentation = "return=representation";
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private readonly PayPalServerSdkClient _client;

    public PayPalPaymentGateway(PayPalServerSdkClient client)
    {
        _client = client;
    }

    public async Task<string> CreateOrderAsync(
        string invoiceId,
        string customId,
        decimal amount,
        string currency,
        string createRequestId,
        CancellationToken ct)
    {
        var body = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new()
                {
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = currency,
                        Value = PayPalMoneyFormat.Format(amount)
                    },
                    CustomId = customId,
                    InvoiceId = invoiceId
                }
            }
        };

        try
        {
            var order = await Once(() => Bounded(
                token => _client.Orders.CreateOrder(
                    payPalMockResponse: null,
                    payPalRequestId: createRequestId,
                    payPalPartnerAttributionId: null,
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: ReturnRepresentation,
                    ct: token),
                ct));

            if (string.IsNullOrWhiteSpace(order.Id))
            {
                throw new PaymentGatewayException("PayPal created an order without an id.", 502);
            }

            return order.Id;
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw MapCreateOrder(ex);
        }
        catch (Exception ex) when (IsBoundary(ex))
        {
            throw TranslateBoundary(ex);
        }
    }

    public async Task<AuthorizationHold> AuthorizeExistingOrderAsync(
        string payPalOrderId,
        CardPaymentSource card,
        string authorizeRequestId,
        CancellationToken ct)
    {
        var body = new OrderAuthorizeRequest
        {
            PaymentSource = new OrderAuthorizeRequestPaymentSource
            {
                Card = BuildCard(card)
            }
        };

        try
        {
            var response = await Once(() => Bounded(
                token => _client.Orders.AuthorizeOrder(
                    id: payPalOrderId,
                    payPalMockResponse: null,
                    payPalRequestId: authorizeRequestId,
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: ReturnRepresentation,
                    ct: token),
                ct));

            GuardChallenge(response.Status, response.PaymentSource?.Card);
            var hold = ExtractHold(payPalOrderId, response.PurchaseUnits, response.Status);
            if (hold is not null)
            {
                return hold;
            }

            var refreshed = await Bounded(
                token => _client.Orders.GetOrder(
                    id: payPalOrderId,
                    fields: null,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    ct: token),
                ct);

            GuardChallenge(refreshed.Status, refreshed.PaymentSource?.Card);
            hold = ExtractHold(payPalOrderId, refreshed.PurchaseUnits, refreshed.Status);
            if (hold is null)
            {
                throw new PaymentGatewayException("PayPal authorized the order but returned no authorization id.", 502);
            }

            return hold;
        }
        catch (PaymentChallengeRequiredException)
        {
            throw;
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            throw MapAuthorizeOrder(ex);
        }
        catch (Exception ex) when (IsBoundary(ex))
        {
            throw TranslateBoundary(ex);
        }
    }

    public async Task<AuthorizationSnapshot> GetAuthorizationAsync(string authorizationId, CancellationToken ct)
    {
        try
        {
            var auth = await Bounded(
                token => _client.Payments.GetAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    ct: token),
                ct);

            return new AuthorizationSnapshot
            {
                AuthorizationId = auth.Id ?? authorizationId,
                Status = auth.Status?.Value ?? string.Empty,
                ExpirationTime = PayPalMoneyFormat.ParseTime(auth.ExpirationTime),
                CreateTime = PayPalMoneyFormat.ParseTime(auth.CreateTime),
                AmountValue = auth.Amount?.Value,
                Currency = auth.Amount?.CurrencyCode
            };
        }
        catch (SdkException<GetAuthorizedPaymentError> ex)
        {
            throw MapGetAuthorizedPayment(ex);
        }
        catch (Exception ex) when (IsBoundary(ex))
        {
            throw TranslateBoundary(ex);
        }
    }

    public async Task<AuthorizationHold> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken ct)
    {
        var body = new ReauthorizeRequest
        {
            Amount = new Money
            {
                CurrencyCode = currency,
                Value = PayPalMoneyFormat.Format(amount)
            }
        };

        try
        {
            var auth = await Once(() => Bounded(
                token => _client.Payments.ReauthorizePayment(
                    authorizationId: authorizationId,
                    payPalRequestId: requestId,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: ReturnRepresentation,
                    ct: token),
                ct));

            if (string.IsNullOrWhiteSpace(auth.Id))
            {
                throw new PaymentGatewayException("PayPal reauthorized the payment but returned no authorization id.", 502);
            }

            return new AuthorizationHold
            {
                PayPalOrderId = string.Empty,
                AuthorizationId = auth.Id,
                Status = auth.Status?.Value ?? string.Empty,
                AmountValue = auth.Amount?.Value,
                Currency = auth.Amount?.CurrencyCode,
                ExpirationTime = PayPalMoneyFormat.ParseTime(auth.ExpirationTime),
                CreateTime = PayPalMoneyFormat.ParseTime(auth.CreateTime)
            };
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            throw MapReauthorizePayment(ex);
        }
        catch (Exception ex) when (IsBoundary(ex))
        {
            throw TranslateBoundary(ex);
        }
    }

    public async Task<CaptureResult> CaptureAsync(
        string authorizationId,
        string requestId,
        string? invoiceId,
        CancellationToken ct)
    {
        var body = new CaptureRequest
        {
            FinalCapture = true,
            InvoiceId = invoiceId
        };

        try
        {
            var captured = await Once(() => Bounded(
                token => _client.Payments.CaptureAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalRequestId: requestId,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: ReturnRepresentation,
                    ct: token),
                ct));

            return MapCapture(captured);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            throw MapCaptureAuthorizedPayment(ex);
        }
        catch (Exception ex) when (IsBoundary(ex))
        {
            throw TranslateBoundary(ex);
        }
    }

    public async Task<CaptureResult> GetCaptureAsync(string captureId, CancellationToken ct)
    {
        try
        {
            var captured = await Bounded(
                token => _client.Payments.GetCapturedPayment(
                    captureId: captureId,
                    payPalMockResponse: null,
                    ct: token),
                ct);
            return MapCapture(captured);
        }
        catch (SdkException<GetCapturedPaymentError> ex)
        {
            throw MapGetCapturedPayment(ex);
        }
        catch (Exception ex) when (IsBoundary(ex))
        {
            throw TranslateBoundary(ex);
        }
    }

    public async Task<string> VoidAsync(string authorizationId, string requestId, CancellationToken ct)
    {
        try
        {
            var auth = await Once(() => Bounded(
                token => _client.Payments.VoidPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    payPalRequestId: requestId,
                    prefer: ReturnRepresentation,
                    ct: token),
                ct));

            return auth.Status?.Value ?? "VOIDED";
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            throw MapVoidPayment(ex);
        }
        catch (Exception ex) when (IsBoundary(ex))
        {
            throw TranslateBoundary(ex);
        }
    }

    public async Task<RefundGatewayResult> RefundAsync(
        string captureId,
        decimal? amount,
        string? currency,
        string requestId,
        CancellationToken ct)
    {
        RefundRequest? body = null;
        if (amount is decimal refundAmount)
        {
            body = new RefundRequest
            {
                Amount = new Money
                {
                    CurrencyCode = currency ?? string.Empty,
                    Value = PayPalMoneyFormat.Format(refundAmount)
                }
            };
        }

        try
        {
            var refund = await Once(() => Bounded(
                token => _client.Payments.RefundCapturedPayment(
                    captureId: captureId,
                    payPalMockResponse: null,
                    payPalRequestId: requestId,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: ReturnRepresentation,
                    ct: token),
                ct));

            if (string.IsNullOrWhiteSpace(refund.Id))
            {
                throw new PaymentGatewayException("PayPal refunded the capture but returned no refund id.", 502);
            }

            return new RefundGatewayResult
            {
                RefundId = refund.Id,
                Status = refund.Status?.Value ?? string.Empty,
                Amount = PayPalMoneyFormat.Parse(refund.Amount?.Value) ?? amount ?? 0m,
                TotalRefundedAmount = PayPalMoneyFormat.Parse(refund.SellerPayableBreakdown?.TotalRefundedAmount?.Value),
                Currency = refund.Amount?.CurrencyCode ?? currency
            };
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            throw MapRefundCapturedPayment(ex);
        }
        catch (Exception ex) when (IsBoundary(ex))
        {
            throw TranslateBoundary(ex);
        }
    }

    public async Task<VaultedCardResult> SaveCardAsync(
        string merchantCustomerId,
        string? payPalCustomerId,
        CardPaymentSource card,
        string requestId,
        CancellationToken ct)
    {
        var body = new PaymentTokenRequest
        {
            Customer = new Customer
            {
                Id = payPalCustomerId,
                MerchantCustomerId = merchantCustomerId
            },
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Name = card.Name,
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    BillingAddress = BuildAddress(card.BillingAddress)
                }
            }
        };

        try
        {
            var token = await Once(() => Bounded(
                token => _client.Vault.CreatePaymentToken(
                    payPalRequestId: requestId,
                    body: body,
                    ct: token),
                ct));

            if (token.PaymentSource?.Card?.AuthenticationResult?.ThreeDSecure?.AuthenticationStatus == ParesStatus.C)
            {
                throw new PaymentChallengeRequiredException();
            }

            if (string.IsNullOrWhiteSpace(token.Id))
            {
                throw new PaymentGatewayException("PayPal vaulted the card but returned no payment token id.", 502);
            }

            var cardEntity = token.PaymentSource?.Card;
            return new VaultedCardResult
            {
                PaymentTokenId = token.Id,
                PayPalCustomerId = token.Customer?.Id,
                LastDigits = cardEntity?.LastDigits,
                Brand = cardEntity?.Brand?.Value,
                Expiry = cardEntity?.Expiry,
                Name = cardEntity?.Name
            };
        }
        catch (PaymentChallengeRequiredException)
        {
            throw;
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            throw MapCreatePaymentToken(ex);
        }
        catch (Exception ex) when (IsBoundary(ex))
        {
            throw TranslateBoundary(ex);
        }
    }

    public async Task DeleteCardAsync(string paymentTokenId, CancellationToken ct)
    {
        try
        {
            await Once(() => Bounded(
                token => _client.Vault.DeletePaymentToken(id: paymentTokenId, ct: token),
                ct));
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            throw MapDeletePaymentToken(ex);
        }
        catch (Exception ex) when (IsBoundary(ex))
        {
            throw TranslateBoundary(ex);
        }
    }

    public async Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct)
    {
        var utcFrom = from.ToUniversalTime();
        var utcTo = to.ToUniversalTime();
        if (utcTo > DateTimeOffset.UtcNow)
        {
            utcTo = DateTimeOffset.UtcNow;
        }

        if (utcTo < utcFrom)
        {
            utcTo = utcFrom;
        }

        var startDate = PayPalMoneyFormat.FormatSearchInstant(utcFrom);
        var endDate = PayPalMoneyFormat.FormatSearchInstant(utcTo);
        var page = 1;
        var rows = new List<PayPalTransactionRecord>();
        int totalPages;

        try
        {
            do
            {
                var currentPage = page;
                var response = await Bounded(
                    token => _client.TransactionSearch.SearchTransactions(
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
                        balanceAffectingRecordsOnly: "N",
                        pageSize: 100,
                        page: currentPage,
                        ct: token),
                    ct);

                if (response.TransactionDetails is not null)
                {
                    foreach (var detail in response.TransactionDetails)
                    {
                        var info = detail.TransactionInfo;
                        if (info is null)
                        {
                            continue;
                        }

                        rows.Add(new PayPalTransactionRecord
                        {
                            TransactionId = info.TransactionId,
                            PaypalReferenceId = info.PaypalReferenceId,
                            PaypalReferenceIdType = info.PaypalReferenceIdType?.Value,
                            InvoiceId = info.InvoiceId,
                            CustomField = info.CustomField,
                            Amount = PayPalMoneyFormat.Parse(info.TransactionAmount?.Value),
                            Currency = info.TransactionAmount?.CurrencyCode,
                            FeeAmount = PayPalMoneyFormat.Parse(info.FeeAmount?.Value),
                            Status = info.TransactionStatus,
                            InitiationDate = PayPalMoneyFormat.ParseTime(info.TransactionInitiationDate),
                            UpdatedDate = PayPalMoneyFormat.ParseTime(info.TransactionUpdatedDate)
                        });
                    }
                }

                totalPages = response.TotalPages ?? 1;
                page++;
            } while (page <= totalPages);
        }
        catch (SdkException<RawError> ex)
        {
            var body = ex.Error.ReadAsString();
            var detail = string.IsNullOrWhiteSpace(body)
                ? "PayPal transaction search failed."
                : $"PayPal transaction search failed. {TrimProviderBody(body)}";
            throw new PaymentGatewayException(
                detail,
                MapProviderStatus((int)ex.Error.StatusCode),
                providerName: "PayPal");
        }
        catch (Exception ex) when (IsBoundary(ex))
        {
            throw TranslateBoundary(ex);
        }

        return rows;
    }

    private static CardRequest BuildCard(CardPaymentSource card)
    {
        return new CardRequest
        {
            Name = card.Name,
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            VaultId = card.VaultId,
            BillingAddress = BuildAddress(card.BillingAddress),
            StoredCredential = card.UseStoredCredential
                ? new CardStoredCredential
                {
                    PaymentInitiator = PaymentInitiator.Customer,
                    PaymentType = StoredPaymentSourcePaymentType.Unscheduled,
                    Usage = StoredPaymentSourceUsageType.Subsequent
                }
                : null
        };
    }

    private static PayPalAddress? BuildAddress(CardBillingAddress? address)
    {
        if (address is null)
        {
            return null;
        }

        return new PayPalAddress
        {
            CountryCode = string.IsNullOrWhiteSpace(address.CountryCode) ? "US" : address.CountryCode,
            AddressLine1 = address.AddressLine1,
            AddressLine2 = address.AddressLine2,
            AdminArea1 = address.AdminArea1,
            AdminArea2 = address.AdminArea2,
            PostalCode = address.PostalCode
        };
    }

    private static void GuardChallenge(OrderStatus? status, CardResponse? card)
    {
        if (status == OrderStatus.PayerActionRequired)
        {
            throw new PaymentChallengeRequiredException();
        }

        var pares = card?.AuthenticationResult?.ThreeDSecure?.AuthenticationStatus;
        if (pares == ParesStatus.C)
        {
            throw new PaymentChallengeRequiredException();
        }
    }

    private static AuthorizationHold? ExtractHold(
        string payPalOrderId,
        IReadOnlyList<PurchaseUnit>? units,
        OrderStatus? orderStatus)
    {
        var auth = units?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
        if (auth is null || string.IsNullOrWhiteSpace(auth.Id))
        {
            return null;
        }

        return new AuthorizationHold
        {
            PayPalOrderId = payPalOrderId,
            AuthorizationId = auth.Id,
            Status = auth.Status?.Value ?? orderStatus?.Value ?? string.Empty,
            AmountValue = auth.Amount?.Value,
            Currency = auth.Amount?.CurrencyCode,
            ExpirationTime = PayPalMoneyFormat.ParseTime(auth.ExpirationTime),
            CreateTime = PayPalMoneyFormat.ParseTime(auth.CreateTime)
        };
    }

    private static CaptureResult MapCapture(CapturedPayment captured)
    {
        if (string.IsNullOrWhiteSpace(captured.Id))
        {
            throw new PaymentGatewayException("PayPal captured the payment but returned no capture id.", 502);
        }

        var breakdown = captured.SellerReceivableBreakdown;
        return new CaptureResult
        {
            CaptureId = captured.Id,
            Status = captured.Status?.Value ?? string.Empty,
            GrossAmount = PayPalMoneyFormat.Parse(breakdown?.GrossAmount.Value) ?? PayPalMoneyFormat.Parse(captured.Amount?.Value),
            PaypalFee = PayPalMoneyFormat.Parse(breakdown?.PaypalFee?.Value),
            NetAmount = PayPalMoneyFormat.Parse(breakdown?.NetAmount?.Value),
            Currency = breakdown?.GrossAmount.CurrencyCode ?? captured.Amount?.CurrencyCode
        };
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private async Task Bounded(Func<CancellationToken, Task> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        await call(cts.Token);
    }

    private static async Task<T> Once<T>(Func<Task<T>> call)
    {
        PayPalWriteScope.Begin();
        PayPalLastStatus.Value = null;
        try
        {
            return await call();
        }
        catch (PayPalDuplicateSendException)
        {
            throw new PaymentGatewayException(
                "The payment request may already have reached PayPal. Retry the same order; do not submit a new payment.",
                409,
                unknownOutcome: true);
        }
    }

    private static async Task Once(Func<Task> call)
    {
        PayPalWriteScope.Begin();
        PayPalLastStatus.Value = null;
        try
        {
            await call();
        }
        catch (PayPalDuplicateSendException)
        {
            throw new PaymentGatewayException(
                "The payment request may already have reached PayPal. Retry the same order; do not submit a new payment.",
                409,
                unknownOutcome: true);
        }
    }

    private static bool IsBoundary(Exception ex) =>
        ex is JsonException
            or HttpRequestException
            or TaskCanceledException
            or PayPalDuplicateSendException
            or AuthSchemeException
            or PaymentGatewayException;

    private static PaymentGatewayException TranslateBoundary(Exception ex)
    {
        if (ex is PaymentGatewayException gateway)
        {
            return gateway;
        }

        if (ex is PayPalDuplicateSendException)
        {
            return new PaymentGatewayException(
                "The payment request may already have reached PayPal. Retry the same order; do not submit a new payment.",
                409,
                unknownOutcome: true);
        }

        if (ex is JsonException)
        {
            var status = PayPalLastStatus.Value;
            if (status is >= 400 and < 500)
            {
                return new PaymentGatewayException("PayPal rejected the request.", MapProviderStatus(status.Value));
            }

            return new PaymentGatewayException("The payment processor returned a response that could not be processed.", 502);
        }

        if (ex is HttpRequestException or TaskCanceledException)
        {
            return new PaymentGatewayException("The payment processor is unreachable.", 502);
        }

        if (ex is AuthSchemeException)
        {
            return new PaymentGatewayException("PayPal authentication is not configured correctly.", 502);
        }

        return new PaymentGatewayException("The payment processor request failed.", 502);
    }

    private static PaymentGatewayException MapCreateOrder(SdkException<CreateOrderError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return FromError(error, 400);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return FromRaw(raw);
        }

        return new PaymentGatewayException("PayPal rejected the order create request.", 400);
    }

    private static PaymentGatewayException MapAuthorizeOrder(SdkException<AuthorizeOrderError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return FromError(error, 422);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return FromRaw(raw);
        }

        return new PaymentGatewayException("PayPal rejected the authorization.", 422);
    }

    private static PaymentGatewayException MapGetAuthorizedPayment(SdkException<GetAuthorizedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return FromError(error, 404);
        }

        if (ex.Error.TryGetNoContent(out var noContent))
        {
            return FromRaw(noContent);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return FromRaw(raw);
        }

        return new PaymentGatewayException("PayPal authorization could not be loaded.", 502);
    }

    private static PaymentGatewayException MapReauthorizePayment(SdkException<ReauthorizePaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return FromError(error, 422);
        }

        if (ex.Error.TryGetNoContent(out var noContent))
        {
            return FromRaw(noContent);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return FromRaw(raw);
        }

        return new PaymentGatewayException("PayPal could not renew the authorization.", 422);
    }

    private static PaymentGatewayException MapCaptureAuthorizedPayment(SdkException<CaptureAuthorizedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return FromError(error, 409);
        }

        if (ex.Error.TryGetNoContent(out var noContent))
        {
            return FromRaw(noContent);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return FromRaw(raw);
        }

        return new PaymentGatewayException("PayPal rejected the capture.", 422);
    }

    private static PaymentGatewayException MapGetCapturedPayment(SdkException<GetCapturedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return FromError(error, 404);
        }

        if (ex.Error.TryGetNoContent(out var noContent))
        {
            return FromRaw(noContent);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return FromRaw(raw);
        }

        return new PaymentGatewayException("PayPal capture could not be loaded.", 502);
    }

    private static PaymentGatewayException MapVoidPayment(SdkException<VoidPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return FromError(error, 409);
        }

        if (ex.Error.TryGetNoContent(out var noContent))
        {
            return FromRaw(noContent);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return FromRaw(raw);
        }

        return new PaymentGatewayException("PayPal rejected the void.", 409);
    }

    private static PaymentGatewayException MapRefundCapturedPayment(SdkException<RefundCapturedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return FromError(error, 422);
        }

        if (ex.Error.TryGetNoContent(out var noContent))
        {
            return FromRaw(noContent);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return FromRaw(raw);
        }

        return new PaymentGatewayException("PayPal rejected the refund.", 422);
    }

    private static PaymentGatewayException MapCreatePaymentToken(SdkException<CreatePaymentTokenError> ex)
    {
        if (ex.Error.TryGetError1(out var error))
        {
            return FromError1(error, 422);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return FromRaw(raw);
        }

        return new PaymentGatewayException("PayPal rejected the saved card.", 422);
    }

    private static PaymentGatewayException MapDeletePaymentToken(SdkException<DeletePaymentTokenError> ex)
    {
        if (ex.Error.TryGetError1(out var error))
        {
            return FromError1(error, 400);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return FromRaw(raw);
        }

        return new PaymentGatewayException("PayPal could not delete the saved card.", 502);
    }

    private static PaymentGatewayException FromError(Error error, int fallbackStatus)
    {
        var status = StatusFromName(error.Name, fallbackStatus);
        return new PaymentGatewayException(FormatError(error.Message, error.Details), status, error.Name, error.DebugId, error.Details?.FirstOrDefault()?.Issue);
    }

    private static PaymentGatewayException FromError1(Error1 error, int fallbackStatus)
    {
        var status = StatusFromName(error.Name, fallbackStatus);
        var details = error.Details?.Select(d => (d.Field, d.Issue, d.Description)).ToList();
        var message = FormatErrorParts(error.Message, details);
        return new PaymentGatewayException(message, status, error.Name, error.DebugId, error.Details?.FirstOrDefault()?.Issue);
    }

    private static string FormatError(string fallback, IReadOnlyList<ErrorDetails>? details)
    {
        if (details is null || details.Count == 0)
        {
            return fallback;
        }

        return FormatErrorParts(fallback, details.Select(d => (d.Field, d.Issue, d.Description)).ToList());
    }

    private static string FormatErrorParts(string fallback, IReadOnlyList<(string? Field, string? Issue, string? Description)>? details)
    {
        if (details is null || details.Count == 0)
        {
            return fallback;
        }

        var parts = details.Select(d =>
        {
            var description = string.IsNullOrWhiteSpace(d.Description) ? fallback : d.Description;
            var field = string.IsNullOrWhiteSpace(d.Field) ? string.Empty : $" ({d.Field})";
            var issue = string.IsNullOrWhiteSpace(d.Issue) ? string.Empty : $" [{d.Issue}]";
            return $"{description}{field}{issue}";
        });
        return string.Join("; ", parts);
    }

    private static string TrimProviderBody(string body)
    {
        var compact = string.Join(" ", body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return compact.Length <= 500 ? compact : compact[..500];
    }

    private static PaymentGatewayException FromRaw(RawError raw)
    {
        return new PaymentGatewayException(
            "The payment processor rejected the request.",
            MapProviderStatus((int)raw.StatusCode));
    }

    private static int StatusFromName(string name, int fallback) => name switch
    {
        "AUTHENTICATION_FAILURE" => 502,
        "NOT_AUTHORIZED" => 403,
        "RESOURCE_NOT_FOUND" => 404,
        "UNPROCESSABLE_ENTITY" => 422,
        "INVALID_REQUEST" => 400,
        _ => MapProviderStatus(fallback)
    };

    private static int MapProviderStatus(int status) => status switch
    {
        401 or 500 or 502 or 503 or 504 => 502,
        >= 400 and < 600 => status,
        _ => 502
    };
}
