using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using PayPalAddress = PayPalServerSdk.Models.Address;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalPaymentGateway : IPayPalPaymentGateway
{
    private const string PreferRepresentation = "return=representation";
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private readonly PayPalServerSdkClient _client;

    public PayPalPaymentGateway(PayPalServerSdkClient client)
    {
        _client = client;
    }

    public Task<AuthorizationResult> AuthorizeCardAsync(
        string orderId,
        decimal amount,
        string currency,
        CardPaymentInput card,
        string createRequestId,
        string authorizeRequestId,
        CancellationToken ct) =>
        AuthorizeAsync(orderId, amount, currency, ToCardRequest(card), createRequestId, authorizeRequestId, ct);

    public Task<AuthorizationResult> AuthorizeVaultedCardAsync(
        string orderId,
        decimal amount,
        string currency,
        string vaultId,
        string createRequestId,
        string authorizeRequestId,
        CancellationToken ct) =>
        AuthorizeAsync(
            orderId,
            amount,
            currency,
            new CardRequest { VaultId = vaultId },
            createRequestId,
            authorizeRequestId,
            ct);

    public async Task<AuthorizationSnapshot> GetAuthorizationAsync(string authorizationId, CancellationToken ct)
    {
        try
        {
            var auth = await Bounded(
                token => _client.Payments.GetAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    requestOptions: null,
                    ct: token),
                ct);

            return ToSnapshot(auth);
        }
        catch (SdkException<GetAuthorizedPaymentError> ex)
        {
            throw MapGetAuthorizedPayment(ex);
        }
        catch (Exception ex) when (IsBoundaryFailure(ex))
        {
            throw MapBoundary(ex);
        }
    }

    public async Task<AuthorizationSnapshot> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken ct)
    {
        try
        {
            using (PayPalAtMostOneWriteHandler.Begin())
            {
                var auth = await Bounded(
                    token => _client.Payments.ReauthorizePayment(
                        authorizationId: authorizationId,
                        payPalRequestId: requestId,
                        payPalAuthAssertion: null,
                        body: new ReauthorizeRequest
                        {
                            Amount = MoneyOf(amount, currency)
                        },
                        prefer: PreferRepresentation,
                        requestOptions: null,
                        ct: token),
                    ct);

                return ToSnapshot(auth);
            }
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            throw MapReauthorize(ex);
        }
        catch (Exception ex) when (IsBoundaryFailure(ex))
        {
            throw MapBoundary(ex);
        }
    }

    public async Task<CaptureResult> CaptureAsync(
        string authorizationId,
        string invoiceId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken ct)
    {
        try
        {
            using (PayPalAtMostOneWriteHandler.Begin())
            {
                var capture = await Bounded(
                    token => _client.Payments.CaptureAuthorizedPayment(
                        authorizationId: authorizationId,
                        payPalMockResponse: null,
                        payPalRequestId: requestId,
                        payPalAuthAssertion: null,
                        body: new CaptureRequest
                        {
                            Amount = MoneyOf(amount, currency),
                            FinalCapture = true,
                            InvoiceId = requestId
                        },
                        prefer: PreferRepresentation,
                        requestOptions: null,
                        ct: token),
                    ct);

                return new CaptureResult(
                    capture.Id ?? throw Missing("capture id"),
                    capture.Status?.Value,
                    capture.Amount?.Value ?? capture.SellerReceivableBreakdown?.GrossAmount.Value,
                    capture.SellerReceivableBreakdown?.PaypalFee?.Value,
                    capture.SellerReceivableBreakdown?.NetAmount?.Value,
                    capture.Amount?.CurrencyCode ?? currency);
            }
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            throw MapCapture(ex);
        }
        catch (Exception ex) when (IsBoundaryFailure(ex))
        {
            throw MapBoundary(ex);
        }
    }

    public async Task VoidAsync(string authorizationId, string requestId, CancellationToken ct)
    {
        try
        {
            using (PayPalAtMostOneWriteHandler.Begin())
            {
                await Bounded(
                    token => _client.Payments.VoidPayment(
                        authorizationId: authorizationId,
                        payPalMockResponse: null,
                        payPalAuthAssertion: null,
                        payPalRequestId: requestId,
                        prefer: PreferRepresentation,
                        requestOptions: null,
                        ct: token),
                    ct);
            }
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            throw MapVoid(ex);
        }
        catch (Exception ex) when (IsBoundaryFailure(ex))
        {
            throw MapBoundary(ex);
        }
    }

    public async Task<RefundGatewayResult> RefundAsync(
        string captureId,
        decimal? amount,
        string currency,
        string requestId,
        CancellationToken ct)
    {
        try
        {
            using (PayPalAtMostOneWriteHandler.Begin())
            {
                RefundRequest? body = amount is null
                    ? null
                    : new RefundRequest { Amount = MoneyOf(amount.Value, currency) };

                var refund = await Bounded(
                    token => _client.Payments.RefundCapturedPayment(
                        captureId: captureId,
                        payPalMockResponse: null,
                        payPalRequestId: requestId,
                        payPalAuthAssertion: null,
                        body: body,
                        prefer: PreferRepresentation,
                        requestOptions: null,
                        ct: token),
                    ct);

                return new RefundGatewayResult(
                    refund.Id ?? throw Missing("refund id"),
                    refund.Status?.Value,
                    refund.Amount?.Value);
            }
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            throw MapRefund(ex);
        }
        catch (Exception ex) when (IsBoundaryFailure(ex))
        {
            throw MapBoundary(ex);
        }
    }

    public async Task<VaultedCardResult> SaveCardAsync(
        string merchantCustomerId,
        CardPaymentInput card,
        string requestId,
        CancellationToken ct)
    {
        try
        {
            using (PayPalAtMostOneWriteHandler.Begin())
            {
                var body = new PaymentTokenRequest
                {
                    Customer = new Customer { MerchantCustomerId = merchantCustomerId },
                    PaymentSource = new PaymentTokenRequestPaymentSource
                    {
                        Card = ToVaultCard(card)
                    }
                };

                var token = await Bounded(
                    t => _client.Vault.CreatePaymentToken(
                        payPalRequestId: requestId,
                        body: body,
                        requestOptions: null,
                        ct: t),
                    ct);

                var cardView = token.PaymentSource?.Card;
                return new VaultedCardResult(
                    token.Id ?? throw Missing("payment token id"),
                    token.Customer?.Id,
                    cardView?.Brand?.Value,
                    cardView?.LastDigits,
                    cardView?.Expiry,
                    cardView?.Name);
            }
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            throw MapCreatePaymentToken(ex);
        }
        catch (Exception ex) when (IsBoundaryFailure(ex))
        {
            throw MapBoundary(ex);
        }
    }

    public async Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken ct)
    {
        try
        {
            using (PayPalAtMostOneWriteHandler.Begin())
            {
                await BoundedVoid(
                    token => _client.Vault.DeletePaymentToken(
                        id: paymentTokenId,
                        requestOptions: null,
                        ct: token),
                    ct);
            }
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            throw MapDeletePaymentToken(ex);
        }
        catch (Exception ex) when (IsBoundaryFailure(ex))
        {
            throw MapBoundary(ex);
        }
    }

    public async Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct)
    {
        var records = new List<PayPalTransactionRecord>();
        try
        {
            foreach (var (start, end) in SplitWindows(from, to))
            {
                var page = 1;
                int? totalPages = null;
                var pageCount = 0;
                do
                {
                    var response = await Bounded(
                        token => _client.TransactionSearch.SearchTransactions(
                            startDate: FormatRfc3339(start),
                            endDate: FormatRfc3339(end),
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
                            ct: token),
                        ct);

                    var pageCountThisPage = 0;
                    if (response.TransactionDetails is not null)
                    {
                        foreach (var detail in response.TransactionDetails)
                        {
                            pageCountThisPage++;
                            var info = detail.TransactionInfo;
                            if (info is null)
                            {
                                continue;
                            }

                            records.Add(new PayPalTransactionRecord(
                                info.TransactionId,
                                info.InvoiceId,
                                info.CustomField,
                                info.TransactionStatus,
                                info.TransactionAmount?.Value,
                                info.TransactionAmount?.CurrencyCode,
                                info.FeeAmount?.Value,
                                info.TransactionEventCode,
                                info.TransactionInitiationDate,
                                info.PaypalReferenceId));
                        }
                    }

                    totalPages = response.TotalPages;
                    pageCount = pageCountThisPage;
                    page++;
                } while (ShouldFetchNextPage(page, totalPages, pageCount));
            }
        }
        catch (SdkException<RawError> ex)
        {
            var status = (int)ex.Error.StatusCode;
            if (status == 404)
            {
                throw new CheckoutException(
                    "PayPal transaction reporting is not available for this merchant (HTTP 404). An empty date range returns HTTP 200 with no transactions.",
                    502,
                    operatorActionable: true);
            }

            throw FromRaw(ex.Error, operatorActionable: true);
        }
        catch (Exception ex) when (IsBoundaryFailure(ex))
        {
            throw MapBoundary(ex);
        }

        return records;
    }

    private async Task<AuthorizationResult> AuthorizeAsync(
        string orderId,
        decimal amount,
        string currency,
        CardRequest card,
        string createRequestId,
        string authorizeRequestId,
        CancellationToken ct)
    {
        var amountValue = PayPalMoney.Format(amount);
        var body = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new[]
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = currency,
                        Value = amountValue
                    },
                    CustomId = orderId,
                    InvoiceId = createRequestId
                }
            },
            PaymentSource = new PaymentSource { Card = card }
        };

        try
        {
            PayPalServerSdk.Models.Order created;
            using (PayPalAtMostOneWriteHandler.Begin())
            {
                created = await Bounded(
                    token => _client.Orders.CreateOrder(
                        payPalMockResponse: null,
                        payPalRequestId: createRequestId,
                        payPalPartnerAttributionId: null,
                        payPalClientMetadataId: null,
                        payPalAuthAssertion: null,
                        body: body,
                        prefer: PreferRepresentation,
                        requestOptions: null,
                        ct: token),
                    ct);
            }

            RejectPayerAction(created.Status);

            var existing = TryReadAuthorization(created.Id, created.PurchaseUnits, amountValue, currency);
            if (existing is not null)
            {
                return existing;
            }

            PayPalServerSdk.Models.OrderAuthorizeResponse authorized;
            using (PayPalAtMostOneWriteHandler.Begin())
            {
                authorized = await Bounded(
                    token => _client.Orders.AuthorizeOrder(
                        id: created.Id ?? throw Missing("PayPal order id"),
                        payPalMockResponse: null,
                        payPalRequestId: authorizeRequestId,
                        payPalClientMetadataId: null,
                        payPalAuthAssertion: null,
                        body: null,
                        prefer: PreferRepresentation,
                        requestOptions: null,
                        ct: token),
                    ct);
            }

            RejectPayerAction(authorized.Status);

            return ReadAuthorization(authorized.Id ?? created.Id, authorized.PurchaseUnits, amountValue, currency);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw MapCreateOrder(ex);
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            throw MapAuthorizeOrder(ex);
        }
        catch (CheckoutException)
        {
            throw;
        }
        catch (Exception ex) when (IsBoundaryFailure(ex))
        {
            throw MapBoundary(ex);
        }
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private async Task BoundedVoid(Func<CancellationToken, Task> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        await call(cts.Token);
    }

    private static void RejectPayerAction(OrderStatus? status)
    {
        if (status == OrderStatus.PayerActionRequired)
        {
            throw new CheckoutException(
                "This card requires a shopper challenge that this application does not support. Use a different card.",
                422);
        }
    }

    private static AuthorizationResult ReadAuthorization(
        string? paypalOrderId,
        IReadOnlyList<PurchaseUnit>? units,
        string amountValue,
        string currency)
    {
        return TryReadAuthorization(paypalOrderId, units, amountValue, currency)
               ?? throw Missing("authorization id");
    }

    private static AuthorizationResult? TryReadAuthorization(
        string? paypalOrderId,
        IReadOnlyList<PurchaseUnit>? units,
        string amountValue,
        string currency)
    {
        var auth = units?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
        if (auth?.Id is null || paypalOrderId is null)
        {
            return null;
        }

        return new AuthorizationResult(
            paypalOrderId,
            auth.Id,
            auth.Status?.Value,
            auth.ExpirationTime,
            auth.CreateTime,
            auth.Amount?.Value ?? amountValue,
            auth.Amount?.CurrencyCode ?? currency);
    }

    private static AuthorizationSnapshot ToSnapshot(PaymentAuthorization auth) =>
        new(
            auth.Id ?? throw Missing("authorization id"),
            auth.Status?.Value,
            auth.ExpirationTime,
            auth.CreateTime,
            auth.Amount?.Value,
            auth.Amount?.CurrencyCode);

    private static CardRequest ToCardRequest(CardPaymentInput card) =>
        new()
        {
            Name = card.Name,
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            BillingAddress = ToPayPalAddress(card.BillingAddress)
        };

    private static PaymentTokenRequestCard ToVaultCard(CardPaymentInput card) =>
        new()
        {
            Name = card.Name,
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            BillingAddress = ToPayPalAddress(card.BillingAddress)
        };

    private static PayPalAddress? ToPayPalAddress(BillingAddressInput? address)
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
            AdminArea1 = address.AdminArea1,
            AdminArea2 = address.AdminArea2,
            PostalCode = address.PostalCode
        };
    }

    private static Money MoneyOf(decimal amount, string currency) =>
        new()
        {
            CurrencyCode = currency,
            Value = PayPalMoney.Format(amount)
        };

    private static IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> SplitWindows(DateTimeOffset from, DateTimeOffset to)
    {
        var cursor = from;
        while (cursor <= to)
        {
            var end = cursor.AddDays(30);
            if (end > to)
            {
                end = to;
            }

            yield return (cursor, end);
            if (end >= to)
            {
                yield break;
            }

            cursor = end;
        }
    }

    private static bool ShouldFetchNextPage(int nextPage, int? totalPages, int lastPageCount)
    {
        if (totalPages is int pages)
        {
            return nextPage <= pages;
        }

        return lastPageCount >= 100;
    }

    private static string FormatRfc3339(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static CheckoutException MapCreateOrder(SdkException<CreateOrderError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return FromError(error, 422);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return FromRaw(raw);
        }

        return ProviderRejected();
    }

    private static CheckoutException MapAuthorizeOrder(SdkException<AuthorizeOrderError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return FromError(error, 422);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return FromRaw(raw);
        }

        return ProviderRejected();
    }

    private static CheckoutException MapGetAuthorizedPayment(SdkException<GetAuthorizedPaymentError> ex)
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

        return ProviderRejected();
    }

    private static CheckoutException MapReauthorize(SdkException<ReauthorizePaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return FromError(error, 422, operatorActionable: true);
        }

        if (ex.Error.TryGetNoContent(out var noContent))
        {
            return FromRaw(noContent, operatorActionable: true);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return FromRaw(raw, operatorActionable: true);
        }

        return new CheckoutException(
            "The payment hold could not be renewed. Ask the shopper to pay again.",
            422,
            operatorActionable: true);
    }

    private static CheckoutException MapCapture(SdkException<CaptureAuthorizedPaymentError> ex)
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

        return ProviderRejected();
    }

    private static CheckoutException MapVoid(SdkException<VoidPaymentError> ex)
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

        return ProviderRejected();
    }

    private static CheckoutException MapRefund(SdkException<RefundCapturedPaymentError> ex)
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

        return ProviderRejected();
    }

    private static CheckoutException MapCreatePaymentToken(SdkException<CreatePaymentTokenError> ex)
    {
        if (ex.Error.TryGetError1(out var error))
        {
            return FromError1(error, 422);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return FromRaw(raw);
        }

        return ProviderRejected();
    }

    private static CheckoutException MapDeletePaymentToken(SdkException<DeletePaymentTokenError> ex)
    {
        if (ex.Error.TryGetError1(out var error))
        {
            return FromError1(error, 400);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return FromRaw(raw);
        }

        return ProviderRejected();
    }

    private static CheckoutException FromError(Error error, int fallbackStatus, bool operatorActionable = false)
    {
        var issues = error.Details?.Select(d => d.Issue).Where(i => !string.IsNullOrWhiteSpace(i)).ToList()
                     ?? new List<string>();
        var status = GuessStatus(error.Name, issues, fallbackStatus);
        return new CheckoutException(SafePayPalMessage(error.Name, issues), status, operatorActionable);
    }

    private static CheckoutException FromError1(Error1 error, int fallbackStatus)
    {
        var issues = error.Details?.Select(d => d.Issue).Where(i => !string.IsNullOrWhiteSpace(i)).ToList()
                     ?? new List<string>();
        var status = GuessStatus(error.Name, issues, fallbackStatus);
        return new CheckoutException(SafePayPalMessage(error.Name, issues), status);
    }

    private static CheckoutException FromRaw(RawError raw, bool operatorActionable = false)
    {
        var status = (int)raw.StatusCode;
        if (status < 400)
        {
            status = 502;
        }

        return new CheckoutException(
            status >= 500
                ? "The payment provider is unavailable."
                : "The payment provider rejected the request.",
            status,
            operatorActionable);
    }

    private static int GuessStatus(string name, IReadOnlyList<string> issues, int fallback)
    {
        if (Contains(name, issues, "AUTHENTICATION") || Contains(name, issues, "UNAUTHORIZED"))
        {
            return 401;
        }

        if (Contains(name, issues, "NOT_FOUND") || Contains(name, issues, "RESOURCE_NOT_FOUND"))
        {
            return 404;
        }

        if (Contains(name, issues, "INSTRUMENT_DECLINED") || Contains(name, issues, "UNPROCESSABLE"))
        {
            return 422;
        }

        return fallback;
    }

    private static bool Contains(string name, IReadOnlyList<string> issues, string token) =>
        name.Contains(token, StringComparison.OrdinalIgnoreCase)
        || issues.Any(i => i.Contains(token, StringComparison.OrdinalIgnoreCase));

    private static string SafePayPalMessage(string name, IReadOnlyList<string> issues)
    {
        if (Contains(name, issues, "INSTRUMENT_DECLINED"))
        {
            return "The card was declined.";
        }

        var issue = issues.FirstOrDefault();
        if (!string.IsNullOrEmpty(issue))
        {
            return $"The payment provider rejected the request ({issue}).";
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            return $"The payment provider rejected the request ({name}).";
        }

        return "The payment provider rejected the request.";
    }

    private static CheckoutException ProviderRejected() =>
        new("The payment provider rejected the request.", 422);

    private static CheckoutException Missing(string what) =>
        new($"The payment provider returned a response that could not be processed (missing {what}).", 502);

    private static bool IsBoundaryFailure(Exception ex) =>
        ex is JsonException
        or HttpRequestException
        or TaskCanceledException
        or OperationCanceledException
        or PayPalAtMostOneWriteHandler.DuplicateWriteSentinelException
        or AuthSchemeException;

    private static CheckoutException MapBoundary(Exception ex)
    {
        if (ex is PayPalAtMostOneWriteHandler.DuplicateWriteSentinelException)
        {
            return new CheckoutException(
                "The payment could not be confirmed. Retry the same request.",
                503);
        }

        if (ex is JsonException)
        {
            var status = PayPalLastStatusHandler.Current;
            if (status >= 400)
            {
                return new CheckoutException("The payment provider rejected the request.", status.Value);
            }

            return new CheckoutException("The payment provider returned a response that could not be processed.", 502);
        }

        if (ex is AuthSchemeException)
        {
            return new CheckoutException("The payment provider could not be authenticated.", 502);
        }

        return new CheckoutException("The payment provider is unavailable.", 503);
    }
}
