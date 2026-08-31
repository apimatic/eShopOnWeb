using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PayPalGateway : IPayPalGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private readonly PayPalServerSdkClient _client;

    public PayPalGateway(PayPalServerSdkClient client) => _client = client;

    public async Task<ProviderAuthorization> AuthorizeAsync(int orderId, decimal amount, string currency,
        ProviderCardSource source, string createRequestId, string authorizeRequestId, CancellationToken ct)
    {
        var amountText = MoneyText(amount);
        var order = await ExecuteAsync<Order, CreateOrderError>(callCt => _client.Orders.CreateOrder(
            payPalMockResponse: null,
            payPalRequestId: createRequestId,
            payPalPartnerAttributionId: null,
            payPalClientMetadataId: null,
            payPalAuthAssertion: null,
            body: new OrderRequest
            {
                Intent = CheckoutPaymentIntent.Authorize,
                PurchaseUnits = new[]
                {
                    new PurchaseUnitRequest
                    {
                        ReferenceId = orderId.ToString(CultureInfo.InvariantCulture),
                        InvoiceId = createRequestId,
                        CustomId = orderId.ToString(CultureInfo.InvariantCulture),
                        Amount = new AmountWithBreakdown { CurrencyCode = currency, Value = amountText }
                    }
                }
            },
            prefer: "return=representation", ct: callCt), From, true, ct);

        if (string.IsNullOrWhiteSpace(order.Id))
            throw InvalidProviderResponse("PayPal did not return an order id.");

        var card = source.VaultId is not null
            ? new CardRequest { VaultId = source.VaultId }
            : DirectCard(source.Card ?? throw new PaymentApiException(400, "Card details are required."));
        var authorized = await ExecuteAsync<OrderAuthorizeResponse, AuthorizeOrderError>(callCt => _client.Orders.AuthorizeOrder(
            id: order.Id,
            payPalMockResponse: null,
            payPalRequestId: authorizeRequestId,
            payPalClientMetadataId: null,
            payPalAuthAssertion: null,
            body: new OrderAuthorizeRequest
            {
                PaymentSource = new OrderAuthorizeRequestPaymentSource { Card = card }
            },
            prefer: "return=representation", ct: callCt), From, true, ct);

        if (IsChallenge(authorized.Status, authorized.Links))
            throw new PaymentApiException(409,
                "PayPal requires browser approval for this card payment. No approval round-trip is supported.");
        var authorization = authorized.PurchaseUnits?
            .SelectMany(p => p.Payments?.Authorizations ?? Array.Empty<AuthorizationWithAdditionalData>())
            .FirstOrDefault() ?? throw InvalidProviderResponse("PayPal did not return an authorization.");
        return new ProviderAuthorization(order.Id, authorized.Status?.Value,
            Required(authorization.Id, "authorization id"), authorization.Status?.Value ?? "UNKNOWN",
            ParseMoney(authorization.Amount, "authorization"), Currency(authorization.Amount, "authorization"),
            ParseDate(authorization.ExpirationTime));
    }

    public async Task<ProviderAuthorizationStatus> GetAuthorizationAsync(string authorizationId, CancellationToken ct)
    {
        var result = await ExecuteAsync<PaymentAuthorization, GetAuthorizedPaymentError>(callCt => _client.Payments.GetAuthorizedPayment(
            authorizationId: authorizationId, payPalMockResponse: null, payPalAuthAssertion: null, ct: callCt),
            From, false, ct);
        return Authorization(result);
    }

    public async Task<ProviderAuthorizationStatus> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken ct)
    {
        var result = await ExecuteAsync<PaymentAuthorization, ReauthorizePaymentError>(callCt => _client.Payments.ReauthorizePayment(
            authorizationId: authorizationId, payPalRequestId: requestId, payPalAuthAssertion: null,
            body: new ReauthorizeRequest { Amount = Money(amount, currency) },
            prefer: "return=representation", ct: callCt), From, true, ct);
        return Authorization(result);
    }

    public async Task<ProviderCapture> CaptureAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken ct)
    {
        var result = await ExecuteAsync<CapturedPayment, CaptureAuthorizedPaymentError>(callCt => _client.Payments.CaptureAuthorizedPayment(
            authorizationId: authorizationId, payPalMockResponse: null, payPalRequestId: requestId,
            payPalAuthAssertion: null,
            body: new CaptureRequest { Amount = Money(amount, currency), FinalCapture = true },
            prefer: "return=representation", ct: callCt), From, true, ct);
        return Capture(result);
    }

    public async Task<ProviderCapture> GetCaptureAsync(string captureId, CancellationToken ct)
    {
        var result = await ExecuteAsync<CapturedPayment, GetCapturedPaymentError>(callCt => _client.Payments.GetCapturedPayment(
            captureId: captureId, payPalMockResponse: null, ct: callCt), From, false, ct);
        return Capture(result);
    }

    public async Task<ProviderAuthorizationStatus> VoidAsync(string authorizationId, string requestId,
        CancellationToken ct)
    {
        var result = await ExecuteAsync<PaymentAuthorization, VoidPaymentError>(callCt => _client.Payments.VoidPayment(
            authorizationId: authorizationId, payPalMockResponse: null, payPalAuthAssertion: null,
            payPalRequestId: requestId, prefer: "return=representation", ct: callCt), From, true, ct);
        return Authorization(result);
    }

    public async Task<ProviderRefund> RefundAsync(string captureId, decimal amount, string currency,
        bool fullRemainingRefund, string requestId, CancellationToken ct)
    {
        var body = fullRemainingRefund ? new RefundRequest() : new RefundRequest { Amount = Money(amount, currency) };
        var result = await ExecuteAsync<Refund, RefundCapturedPaymentError>(callCt => _client.Payments.RefundCapturedPayment(
            captureId: captureId, payPalMockResponse: null, payPalRequestId: requestId,
            payPalAuthAssertion: null, body: body, prefer: "return=representation", ct: callCt),
            From, true, ct);
        return Refund(result, amount, currency);
    }

    public async Task<ProviderRefund> GetRefundAsync(string refundId, CancellationToken ct)
    {
        var result = await ExecuteAsync<Refund, GetRefundError>(callCt => _client.Payments.GetRefund(
            refundId: refundId, payPalMockResponse: null, payPalAuthAssertion: null, ct: callCt),
            From, false, ct);
        return Refund(result, 0, string.Empty);
    }

    public async Task<ProviderPaymentMethod> SaveCardAsync(string shopperId, CardInput card,
        string requestId, CancellationToken ct)
    {
        var merchantCustomerId = MerchantCustomerId(shopperId);
        var setup = await ExecuteAsync<SetupTokenResponse, CreateSetupTokenError>(callCt => _client.Vault.CreateSetupToken(
            payPalRequestId: requestId + "-setup",
            body: new SetupTokenRequest
            {
                Customer = new Customer { MerchantCustomerId = merchantCustomerId },
                PaymentSource = new SetupTokenRequestPaymentSource
                {
                    Card = new SetupTokenRequestCard
                    {
                        Name = card.Name,
                        Number = card.Number,
                        Expiry = card.Expiry,
                        SecurityCode = card.SecurityCode,
                        BillingAddress = BillingAddress(card.BillingAddress)
                    }
                }
            }, ct: callCt), From, true, ct);

        if (setup.Status == PaymentTokenStatus.PayerActionRequired ||
            setup.Links?.Any(l => string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase)
                || string.Equals(l.Rel, "approve", StringComparison.OrdinalIgnoreCase)) == true)
            throw new PaymentApiException(409,
                "PayPal requires browser approval to save this card. No approval round-trip is supported.");
        var setupId = Required(setup.Id, "setup token id");
        var token = await ExecuteAsync<PaymentTokenResponse, CreatePaymentTokenError>(callCt => _client.Vault.CreatePaymentToken(
            payPalRequestId: requestId + "-token",
            body: new PaymentTokenRequest
            {
                Customer = new Customer { MerchantCustomerId = merchantCustomerId },
                PaymentSource = new PaymentTokenRequestPaymentSource
                {
                    Token = new VaultTokenRequest { Id = setupId, Type = VaultTokenRequestType.SetupToken }
                }
            }, ct: callCt), From, true, ct);
        return PaymentMethod(token);
    }

    public async Task<IReadOnlyList<ProviderPaymentMethod>> ListCardsAsync(string customerId, CancellationToken ct)
    {
        const int pageSize = 5;
        var result = new List<ProviderPaymentMethod>();
        var page = 1;
        while (true)
        {
            var response = await ExecuteAsync<CustomerVaultPaymentTokensResponse, ListCustomerPaymentTokensError>(callCt => _client.Vault.ListCustomerPaymentTokens(
                customerId: customerId, pageSize: pageSize, page: page, totalRequired: true, ct: callCt),
                From, false, ct);
            var items = response.PaymentTokens ?? Array.Empty<PaymentTokenResponse>();
            result.AddRange(items.Select(PaymentMethod));
            if (response.TotalPages is int totalPages && page >= totalPages) break;
            if (response.TotalPages is null && items.Count < pageSize) break;
            page++;
            if (page > 10000) throw InvalidProviderResponse("PayPal vault pagination did not terminate.");
        }
        return result;
    }

    public Task DeleteCardAsync(string tokenId, CancellationToken ct) => ExecuteVoidAsync<DeletePaymentTokenError>(
        callCt => _client.Vault.DeletePaymentToken(id: tokenId, ct: callCt), From, true, ct);

    public async Task<IReadOnlyList<ProviderTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken ct)
    {
        var transactions = new List<ProviderTransaction>();
        var page = 1;
        while (true)
        {
            var response = await ExecuteAsync<SearchResponse, RawError>(callCt => _client.TransactionSearch.SearchTransactions(
                startDate: from.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                endDate: to.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                transactionId: null, transactionType: null, transactionStatus: null,
                transactionAmount: null, transactionCurrency: null, paymentInstrumentType: null,
                storeId: null, terminalId: null, fields: "transaction_info",
                balanceAffectingRecordsOnly: "Y", pageSize: 100, page: page, ct: callCt),
                Raw, false, ct);
            var details = response.TransactionDetails ?? Array.Empty<TransactionDetails>();
            transactions.AddRange(details.Where(d => d.TransactionInfo is not null)
                .Select(d => Transaction(d.TransactionInfo!)));
            if (response.TotalPages is int totalPages && page >= totalPages) break;
            if (response.TotalPages is null && details.Count < 100) break;
            if (response.Page is int providerPage && providerPage < page)
                throw InvalidProviderResponse("PayPal transaction pagination did not advance.");
            page++;
            if (page > 10000) throw InvalidProviderResponse("PayPal transaction pagination did not terminate.");
        }
        return transactions;
    }

    private async Task<T> ExecuteAsync<T, TError>(Func<CancellationToken, Task<T>> call,
        Func<TError, PaymentApiException> errorMap, bool write, CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(CallBudget);
        try { return await call(budget.Token); }
        catch (SdkException<TError> ex) { throw errorMap(ex.Error); }
        catch (JsonException ex) { throw new PaymentApiException(502,
            "PayPal returned a response that could not be processed.", innerException: ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentApiException(503,
                write ? "The PayPal operation did not return a conclusive response; retrying with the same request is safe."
                    : "PayPal is temporarily unreachable.", outcomeUnknown: write, innerException: ex);
        }
    }

    private async Task ExecuteVoidAsync<TError>(Func<CancellationToken, Task> call,
        Func<TError, PaymentApiException> errorMap, bool write, CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(CallBudget);
        try { await call(budget.Token); }
        catch (SdkException<TError> ex) { throw errorMap(ex.Error); }
        catch (JsonException ex) { throw new PaymentApiException(502,
            "PayPal returned a response that could not be processed.", innerException: ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentApiException(503,
                write ? "The PayPal operation did not return a conclusive response; retrying with the same request is safe."
                    : "PayPal is temporarily unreachable.", outcomeUnknown: write, innerException: ex);
        }
    }

    private static CardRequest DirectCard(CardInput card) => new()
    {
        Name = card.Name, Number = card.Number, Expiry = card.Expiry, SecurityCode = card.SecurityCode,
        BillingAddress = BillingAddress(card.BillingAddress)
    };

    private static Address BillingAddress(ApiAddress address) => new()
    {
        AddressLine1 = address.Street, AdminArea2 = address.City, AdminArea1 = address.State,
        PostalCode = address.PostalCode, CountryCode = address.CountryCode
    };

    private static Money Money(decimal amount, string currency) => new()
    { CurrencyCode = currency, Value = MoneyText(amount) };

    private static string MoneyText(decimal amount) => amount.ToString("F2", CultureInfo.InvariantCulture);

    private static ProviderAuthorizationStatus Authorization(PaymentAuthorization value) => new(
        Required(value.Id, "authorization id"), value.Status?.Value ?? "UNKNOWN",
        ParseMoney(value.Amount, "authorization"), Currency(value.Amount, "authorization"),
        ParseDate(value.ExpirationTime));

    private static ProviderCapture Capture(CapturedPayment value)
    {
        var breakdown = value.SellerReceivableBreakdown;
        return new ProviderCapture(Required(value.Id, "capture id"), value.Status?.Value ?? "UNKNOWN",
            ParseMoney(value.Amount, "capture"), Currency(value.Amount, "capture"),
            ParseOptionalMoney(breakdown?.PaypalFee), ParseOptionalMoney(breakdown?.NetAmount));
    }

    private static ProviderRefund Refund(Refund value, decimal fallbackAmount, string fallbackCurrency) => new(
        Required(value.Id, "refund id"), value.Status?.Value ?? "UNKNOWN",
        value.Amount is null ? fallbackAmount : ParseMoney(value.Amount, "refund"),
        value.Amount?.CurrencyCode ?? fallbackCurrency);

    private static ProviderPaymentMethod PaymentMethod(PaymentTokenResponse value)
    {
        var card = value.PaymentSource?.Card;
        return new ProviderPaymentMethod(Required(value.Id, "payment token id"),
            Required(value.Customer?.Id, "PayPal customer id"), card?.Name, card?.Brand?.Value,
            card?.LastDigits, card?.Expiry);
    }

    private static ProviderTransaction Transaction(TransactionInformation value) => new(
        value.TransactionId, value.PaypalReferenceId, value.TransactionEventCode,
        ParseDate(value.TransactionInitiationDate), ParseDate(value.TransactionUpdatedDate),
        ParseOptionalMoney(value.TransactionAmount), ParseOptionalMoney(value.FeeAmount),
        value.TransactionAmount?.CurrencyCode, value.TransactionStatus, value.InvoiceId, value.CustomField);

    private static decimal ParseMoney(Money? money, string operation)
    {
        if (money is null || !decimal.TryParse(money.Value, NumberStyles.Number,
                CultureInfo.InvariantCulture, out var value))
            throw InvalidProviderResponse($"PayPal returned an invalid {operation} amount.");
        return value;
    }

    private static decimal? ParseOptionalMoney(Money? money) => money is null ? null :
        decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value : throw InvalidProviderResponse("PayPal returned an invalid money amount.");

    private static string Currency(Money? money, string operation) =>
        money?.CurrencyCode ?? throw InvalidProviderResponse($"PayPal omitted the {operation} currency.");

    private static DateTimeOffset? ParseDate(string? value) => string.IsNullOrWhiteSpace(value) ? null :
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed : throw InvalidProviderResponse("PayPal returned an invalid date-time.");

    private static string Required(string? value, string field) => !string.IsNullOrWhiteSpace(value)
        ? value : throw InvalidProviderResponse($"PayPal omitted the {field}.");

    private static bool IsChallenge(OrderStatus? status, IReadOnlyList<LinkDescription>? links) =>
        status == OrderStatus.PayerActionRequired || links?.Any(link =>
            string.Equals(link.Rel, "payer-action", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(link.Rel, "approve", StringComparison.OrdinalIgnoreCase)) == true;

    private static string MerchantCustomerId(string shopperId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(shopperId));
        return "eshop-" + Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }

    private static PaymentApiException InvalidProviderResponse(string message) => new(502, message);

    private delegate bool ErrorAccessor(out Error error);
    private delegate bool Error1Accessor(out Error1 error);
    private delegate bool RawAccessor(out RawError error);

    private static PaymentApiException Typed(ErrorAccessor typed, RawAccessor raw)
    {
        if (typed(out var error)) return Provider(error);
        if (raw(out var rawError)) return Raw(rawError);
        return InvalidProviderResponse("PayPal rejected the request without a readable error.");
    }

    private static PaymentApiException Typed(ErrorAccessor typed, RawAccessor noContent, RawAccessor raw)
    {
        if (typed(out var error)) return Provider(error);
        if (noContent(out var noContentError)) return Raw(noContentError);
        if (raw(out var rawError)) return Raw(rawError);
        return InvalidProviderResponse("PayPal rejected the request without a readable error.");
    }

    private static PaymentApiException Typed(Error1Accessor typed, RawAccessor raw)
    {
        if (typed(out var error)) return Provider(error);
        if (raw(out var rawError)) return Raw(rawError);
        return InvalidProviderResponse("PayPal rejected the request without a readable error.");
    }

    private static PaymentApiException Provider(Error error)
    {
        var detail = error.Details?.FirstOrDefault();
        var suffix = detail is null ? string.Empty : $" {detail.Issue}: {detail.Description}";
        return new PaymentApiException(422, $"PayPal {error.Name}: {error.Message}{suffix}", error.DebugId);
    }

    private static PaymentApiException Provider(Error1 error)
    {
        var detail = error.Details?.FirstOrDefault();
        var suffix = detail is null ? string.Empty : $" {detail.Issue}: {detail.Description}";
        return new PaymentApiException(422, $"PayPal {error.Name}: {error.Message}{suffix}", error.DebugId);
    }

    private static PaymentApiException Raw(RawError error)
    {
        var status = (int)error.StatusCode;
        return new PaymentApiException(status is >= 400 and < 500 ? status : 502,
            $"PayPal returned HTTP {status} for the payment operation.");
    }

    private static PaymentApiException From(CreateOrderError e) => Typed(e.TryGetError, e.TryGetRawError);
    private static PaymentApiException From(AuthorizeOrderError e) => Typed(e.TryGetError, e.TryGetRawError);
    private static PaymentApiException From(GetOrderError e) => Typed(e.TryGetError, e.TryGetRawError);
    private static PaymentApiException From(GetAuthorizedPaymentError e) => Typed(e.TryGetError, e.TryGetNoContent, e.TryGetRawError);
    private static PaymentApiException From(ReauthorizePaymentError e) => Typed(e.TryGetError, e.TryGetNoContent, e.TryGetRawError);
    private static PaymentApiException From(CaptureAuthorizedPaymentError e) => Typed(e.TryGetError, e.TryGetNoContent, e.TryGetRawError);
    private static PaymentApiException From(GetCapturedPaymentError e) => Typed(e.TryGetError, e.TryGetNoContent, e.TryGetRawError);
    private static PaymentApiException From(VoidPaymentError e) => Typed(e.TryGetError, e.TryGetNoContent, e.TryGetRawError);
    private static PaymentApiException From(RefundCapturedPaymentError e) => Typed(e.TryGetError, e.TryGetNoContent, e.TryGetRawError);
    private static PaymentApiException From(GetRefundError e) => Typed(e.TryGetError, e.TryGetNoContent, e.TryGetRawError);
    private static PaymentApiException From(CreateSetupTokenError e) => Typed(e.TryGetError1, e.TryGetRawError);
    private static PaymentApiException From(GetSetupTokenError e) => Typed(e.TryGetError1, e.TryGetRawError);
    private static PaymentApiException From(CreatePaymentTokenError e) => Typed(e.TryGetError1, e.TryGetRawError);
    private static PaymentApiException From(GetPaymentTokenError e) => Typed(e.TryGetError1, e.TryGetRawError);
    private static PaymentApiException From(ListCustomerPaymentTokensError e) => Typed(e.TryGetError1, e.TryGetRawError);
    private static PaymentApiException From(DeletePaymentTokenError e) => Typed(e.TryGetError1, e.TryGetRawError);
}
