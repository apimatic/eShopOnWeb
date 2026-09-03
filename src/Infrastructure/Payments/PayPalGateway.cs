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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using PayPalServerSdk;
using PayPalServerSdk.Core;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Core.Hooks;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalGateway
{
    private static readonly TimeSpan TotalCallBudget = TimeSpan.FromSeconds(30);
    private readonly PayPalServerSdkClient _client;

    public PayPalGateway(PayPalServerSdkClient client) => _client = client;

    public async Task<PayPalOrderResult> CreateOrderAsync(int orderId, Guid paymentReference, decimal amount, string currency,
        CardInput? card, string? vaultId, CancellationToken ct)
    {
        HttpStatusCode? status = null;
        try
        {
            var result = await BoundedAsync(deadline => _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: OperationKey(paymentReference, "create"),
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
                            Amount = new AmountWithBreakdown
                            {
                                CurrencyCode = currency,
                                Value = MoneyValue(amount)
                            },
                            CustomId = orderId.ToString(CultureInfo.InvariantCulture),
                            InvoiceId = InvoiceId(paymentReference)
                        }
                    ],
                    PaymentSource = new PaymentSource
                    {
                        Card = card is null
                            ? new CardRequest { VaultId = vaultId }
                            : ToCardRequest(card)
                    }
                },
                prefer: "return=representation",
                requestOptions: CaptureStatus(x => status = x),
                ct: deadline), ct);

            if (result.Status == OrderStatus.PayerActionRequired)
                throw new PaymentWorkflowException(409, "PAYER_ACTION_REQUIRED",
                    "PayPal requires shopper approval in a browser; this API does not implement an approval round-trip.");
            if (string.IsNullOrWhiteSpace(result.Id))
                throw InvalidProviderResponse("PayPal did not return an order identifier.");
            var embedded = result.PurchaseUnits?
                .SelectMany(x => x.Payments?.Authorizations ?? Array.Empty<AuthorizationWithAdditionalData>())
                .FirstOrDefault();
            var authorization = embedded is null || string.IsNullOrWhiteSpace(embedded.Id)
                ? null
                : new PayPalAuthorizationResult(embedded.Id, embedded.Status?.Value,
                    ParseDate(embedded.CreateTime), ParseDate(embedded.ExpirationTime), ParseMoney(embedded.Amount));
            return new PayPalOrderResult(result.Id, result.Status?.Value, authorization);
        }
        catch (SdkException<CreateOrderError> ex) { throw Translate(ex.Error, status, ex); }
        catch (Exception ex) when (IsInfrastructureFailure(ex)) { throw TranslateInfrastructure(ex, status); }
    }

    public async Task<PayPalAuthorizationResult> AuthorizeOrderAsync(Guid paymentReference, string payPalOrderId, CancellationToken ct)
    {
        HttpStatusCode? status = null;
        try
        {
            var result = await BoundedAsync(deadline => _client.Orders.AuthorizeOrder(
                id: payPalOrderId,
                payPalMockResponse: null,
                payPalRequestId: OperationKey(paymentReference, "authorize"),
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: null,
                prefer: "return=representation",
                requestOptions: CaptureStatus(x => status = x),
                ct: deadline), ct);

            if (result.Status == OrderStatus.PayerActionRequired)
                throw new PaymentWorkflowException(409, "PAYER_ACTION_REQUIRED",
                    "PayPal requires shopper approval in a browser; this API does not implement an approval round-trip.");
            var authorization = result.PurchaseUnits?
                .SelectMany(x => x.Payments?.Authorizations ?? Array.Empty<AuthorizationWithAdditionalData>())
                .FirstOrDefault();
            if (authorization is null || string.IsNullOrWhiteSpace(authorization.Id))
                throw InvalidProviderResponse("PayPal did not return an authorization.");
            return new PayPalAuthorizationResult(authorization.Id, authorization.Status?.Value,
                ParseDate(authorization.CreateTime), ParseDate(authorization.ExpirationTime), ParseMoney(authorization.Amount));
        }
        catch (SdkException<AuthorizeOrderError> ex) { throw Translate(ex.Error, status, ex); }
        catch (Exception ex) when (IsInfrastructureFailure(ex)) { throw TranslateInfrastructure(ex, status); }
    }

    public async Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken ct)
    {
        HttpStatusCode? status = null;
        try
        {
            var result = await BoundedAsync(deadline => _client.Payments.GetAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                requestOptions: CaptureStatus(x => status = x),
                ct: deadline), ct);
            return AuthorizationResult(result);
        }
        catch (SdkException<GetAuthorizedPaymentError> ex) { throw Translate(ex.Error, status, ex); }
        catch (Exception ex) when (IsInfrastructureFailure(ex)) { throw TranslateInfrastructure(ex, status); }
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(Guid paymentReference, string authorizationId,
        decimal amount, string currency, CancellationToken ct)
    {
        HttpStatusCode? status = null;
        try
        {
            var result = await BoundedAsync(deadline => _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: OperationKey(paymentReference, "reauthorize"),
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest { Amount = Money(amount, currency) },
                prefer: "return=representation",
                requestOptions: CaptureStatus(x => status = x),
                ct: deadline), ct);
            return AuthorizationResult(result);
        }
        catch (SdkException<ReauthorizePaymentError> ex) { throw Translate(ex.Error, status, ex); }
        catch (Exception ex) when (IsInfrastructureFailure(ex)) { throw TranslateInfrastructure(ex, status); }
    }

    public async Task<PayPalCaptureResult> CaptureAsync(Guid paymentReference, string authorizationId,
        decimal amount, string currency, CancellationToken ct)
    {
        HttpStatusCode? status = null;
        try
        {
            var result = await BoundedAsync(deadline => _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: OperationKey(paymentReference, "capture"),
                payPalAuthAssertion: null,
                body: new CaptureRequest
                {
                    Amount = Money(amount, currency),
                    InvoiceId = InvoiceId(paymentReference),
                    FinalCapture = true
                },
                prefer: "return=representation",
                requestOptions: CaptureStatus(x => status = x),
                ct: deadline), ct);
            if (string.IsNullOrWhiteSpace(result.Id) || result.Amount is null)
                throw InvalidProviderResponse("PayPal did not return complete capture details.");
            var breakdown = result.SellerReceivableBreakdown;
            return new PayPalCaptureResult(result.Id, result.Status?.Value,
                ParseMoney(result.Amount), ParseMoneyOptional(breakdown?.PaypalFee), ParseMoneyOptional(breakdown?.NetAmount));
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex) { throw Translate(ex.Error, status, ex); }
        catch (Exception ex) when (IsInfrastructureFailure(ex)) { throw TranslateInfrastructure(ex, status); }
    }

    public async Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken ct)
    {
        HttpStatusCode? status = null;
        try
        {
            var result = await BoundedAsync(deadline => _client.Payments.GetCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                requestOptions: CaptureStatus(x => status = x),
                ct: deadline), ct);
            if (string.IsNullOrWhiteSpace(result.Id) || result.Amount is null)
                throw InvalidProviderResponse("PayPal did not return complete capture details.");
            var breakdown = result.SellerReceivableBreakdown;
            return new PayPalCaptureResult(result.Id, result.Status?.Value,
                ParseMoney(result.Amount), ParseMoneyOptional(breakdown?.PaypalFee), ParseMoneyOptional(breakdown?.NetAmount));
        }
        catch (SdkException<GetCapturedPaymentError> ex) { throw Translate(ex.Error, status, ex); }
        catch (Exception ex) when (IsInfrastructureFailure(ex)) { throw TranslateInfrastructure(ex, status); }
    }

    public async Task<string> VoidAsync(Guid paymentReference, string authorizationId, CancellationToken ct)
    {
        HttpStatusCode? status = null;
        try
        {
            var result = await BoundedAsync(deadline => _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: OperationKey(paymentReference, "void"),
                prefer: "return=representation",
                requestOptions: CaptureStatus(x => status = x),
                ct: deadline), ct);
            return result.Status?.Value ?? "VOIDED";
        }
        catch (SdkException<VoidPaymentError> ex) { throw Translate(ex.Error, status, ex); }
        catch (Exception ex) when (IsInfrastructureFailure(ex)) { throw TranslateInfrastructure(ex, status); }
    }

    public async Task<PayPalRefundResult> RefundAsync(string captureId, decimal amount, string currency,
        string idempotencyKey, Guid paymentReference, CancellationToken ct)
    {
        HttpStatusCode? status = null;
        try
        {
            var result = await BoundedAsync(deadline => _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: RefundOperationKey(paymentReference, idempotencyKey),
                payPalAuthAssertion: null,
                body: new RefundRequest
                {
                    Amount = Money(amount, currency),
                    CustomId = paymentReference.ToString("N")
                },
                prefer: "return=representation",
                requestOptions: CaptureStatus(x => status = x),
                ct: deadline), ct);
            if (string.IsNullOrWhiteSpace(result.Id))
                throw InvalidProviderResponse("PayPal did not return a refund identifier.");
            return new PayPalRefundResult(result.Id, result.Status?.Value ?? "UNKNOWN", ParseMoney(result.Amount));
        }
        catch (SdkException<RefundCapturedPaymentError> ex) { throw Translate(ex.Error, status, ex); }
        catch (Exception ex) when (IsInfrastructureFailure(ex)) { throw TranslateInfrastructure(ex, status); }
    }

    public async Task<PayPalRefundResult> GetRefundAsync(string refundId, CancellationToken ct)
    {
        HttpStatusCode? status = null;
        try
        {
            var result = await BoundedAsync(deadline => _client.Payments.GetRefund(
                refundId: refundId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                requestOptions: CaptureStatus(x => status = x),
                ct: deadline), ct);
            if (string.IsNullOrWhiteSpace(result.Id) || result.Amount is null)
                throw InvalidProviderResponse("PayPal did not return complete refund details.");
            return new PayPalRefundResult(result.Id, result.Status?.Value ?? "UNKNOWN", ParseMoney(result.Amount));
        }
        catch (SdkException<GetRefundError> ex) { throw Translate(ex.Error, status, ex); }
        catch (Exception ex) when (IsInfrastructureFailure(ex)) { throw TranslateInfrastructure(ex, status); }
    }

    public async Task<PayPalPaymentMethodResult> SaveCardAsync(string buyerId, string operationKey,
        CardInput card, CancellationToken ct)
    {
        HttpStatusCode? status = null;
        try
        {
            var result = await BoundedAsync(deadline => _client.Vault.CreatePaymentToken(
                payPalRequestId: operationKey,
                body: new PaymentTokenRequest
                {
                    Customer = new Customer { MerchantCustomerId = CustomerReference(buyerId) },
                    PaymentSource = new PaymentTokenRequestPaymentSource
                    {
                        Card = new PaymentTokenRequestCard
                        {
                            Name = card.Name,
                            Number = Digits(card.Number),
                            Expiry = card.Expiry,
                            SecurityCode = card.SecurityCode,
                            BillingAddress = ToAddress(card.BillingAddress)
                        }
                    }
                },
                requestOptions: CaptureStatus(x => status = x),
                ct: deadline), ct);
            var saved = result.PaymentSource?.Card;
            if (string.IsNullOrWhiteSpace(result.Id) || saved is null || string.IsNullOrWhiteSpace(saved.LastDigits))
                throw InvalidProviderResponse("PayPal did not return a usable saved-card token.");
            return new PayPalPaymentMethodResult(result.Id, saved.LastDigits, saved.Brand?.Value,
                saved.Expiry, result.Customer?.Id);
        }
        catch (SdkException<CreatePaymentTokenError> ex) { throw Translate(ex.Error, status, ex); }
        catch (Exception ex) when (IsInfrastructureFailure(ex)) { throw TranslateInfrastructure(ex, status); }
    }

    public async Task DeleteCardAsync(string vaultId, CancellationToken ct)
    {
        HttpStatusCode? status = null;
        try
        {
            await BoundedAsync(async deadline =>
            {
                await _client.Vault.DeletePaymentToken(vaultId, CaptureStatus(x => status = x), deadline);
                return true;
            }, ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex) { throw Translate(ex.Error, status, ex); }
        catch (Exception ex) when (IsInfrastructureFailure(ex)) { throw TranslateInfrastructure(ex, status); }
    }

    public async Task<SearchResponse> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        int page, CancellationToken ct)
    {
        HttpStatusCode? status = null;
        try
        {
            return await BoundedAsync(deadline => _client.TransactionSearch.SearchTransactions(
                startDate: from.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                endDate: to.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
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
                page: page,
                requestOptions: CaptureStatus(x => status = x),
                ct: deadline), ct);
        }
        catch (SdkException<RawError> ex)
        {
            var error = TryReadError(ex.Error);
            if (error is not null) throw ProviderError(status ?? ex.Error.StatusCode, error, ex);
            throw ProviderError(status ?? ex.Error.StatusCode, "PAYPAL_REPORTING_ERROR",
                "PayPal transaction reporting rejected the request.", null, ex);
        }
        catch (Exception ex) when (IsInfrastructureFailure(ex)) { throw TranslateInfrastructure(ex, status); }
    }

    private static PayPalAuthorizationResult AuthorizationResult(PaymentAuthorization result)
    {
        if (string.IsNullOrWhiteSpace(result.Id))
            throw InvalidProviderResponse("PayPal did not return an authorization identifier.");
        return new PayPalAuthorizationResult(result.Id, result.Status?.Value,
            ParseDate(result.CreateTime), ParseDate(result.ExpirationTime), ParseMoney(result.Amount));
    }

    private static CardRequest ToCardRequest(CardInput card) => new()
    {
        Name = card.Name,
        Number = Digits(card.Number),
        Expiry = card.Expiry,
        SecurityCode = card.SecurityCode,
        BillingAddress = ToAddress(card.BillingAddress)
    };

    private static Address ToAddress(CardAddressInput address) => new()
    {
        AddressLine1 = address.AddressLine1,
        AddressLine2 = address.AddressLine2,
        AdminArea2 = address.City,
        AdminArea1 = address.State,
        PostalCode = address.PostalCode,
        CountryCode = address.CountryCode.ToUpperInvariant()
    };

    private static Money Money(decimal amount, string currency) => new()
    {
        CurrencyCode = currency,
        Value = MoneyValue(amount)
    };

    private static string MoneyValue(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);
    private static string Digits(string value) => new(value.Where(char.IsDigit).ToArray());
    private static string InvoiceId(Guid paymentReference) => $"ESHOP-{paymentReference:N}";
    private static string OperationKey(Guid paymentReference, string operation) => $"eshop-{paymentReference:N}-{operation}";
    private static string RefundOperationKey(Guid paymentReference, string callerKey)
    {
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(callerKey)))[..32];
        return $"eshop-{paymentReference:N}-refund-{digest}";
    }
    private static string CustomerReference(string buyerId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(buyerId))).ToLowerInvariant();

    private static decimal ParseMoney(Money? money) => money is null
        ? 0m
        : decimal.Parse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture);

    private static decimal? ParseMoneyOptional(Money? money) => money is null ? null : ParseMoney(money);

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;

    private static RequestOptions CaptureStatus(Action<HttpStatusCode> capture) => new()
    {
        Hooks = [SdkHook.OnResponse((response, _) => capture(response.StatusCode))]
    };

    private static async Task<T> BoundedAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TotalCallBudget);
        return await call(deadline.Token);
    }

    private static bool IsInfrastructureFailure(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or JsonException;

    private static PaymentWorkflowException TranslateInfrastructure(Exception ex, HttpStatusCode? status)
    {
        if (status is >= HttpStatusCode.BadRequest)
            return ProviderError(status.Value, "PAYPAL_RESPONSE_ERROR", "PayPal rejected the request, but its response could not be processed.", null, ex);
        return new PaymentWorkflowException(502, "PAYPAL_UNAVAILABLE",
            "PayPal is unavailable or returned a response that could not be processed.", innerException: ex);
    }

    private static PaymentWorkflowException InvalidProviderResponse(string message) =>
        new(502, "PAYPAL_INVALID_RESPONSE", message);

    private static Error? TryReadError(RawError error)
    {
        try { return error.ReadAsJson<Error>(); }
        catch (JsonException) { return null; }
    }

    private static PaymentWorkflowException ProviderError(HttpStatusCode? status, Error? error, Exception inner)
    {
        var detail = error?.Details?.FirstOrDefault();
        var code = detail?.Issue ?? error?.Name ?? "PAYPAL_ERROR";
        var message = detail is null
            ? error?.Message ?? "PayPal rejected the request."
            : $"{error!.Message} {detail.Issue}: {detail.Description ?? $"Invalid field {detail.Field}."}";
        return ProviderError(status, code, message, error?.DebugId, inner);
    }

    private static PaymentWorkflowException ProviderError(HttpStatusCode? status, string code, string message,
        string? debugId, Exception inner)
    {
        var httpStatus = status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => 502,
            HttpStatusCode.TooManyRequests => 503,
            >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError => (int)status.Value,
            _ => 502
        };
        return new PaymentWorkflowException(httpStatus, code, message, debugId, inner);
    }

    private static PaymentWorkflowException Translate(CreateOrderError error, HttpStatusCode? status, Exception inner)
    { if (error.TryGetError(out var value)) return ProviderError(status, value, inner); if (error.TryGetRawError(out var raw)) return ProviderError(raw.StatusCode, "PAYPAL_ERROR", "PayPal rejected the request.", null, inner); return ProviderError(status, null, inner); }
    private static PaymentWorkflowException Translate(AuthorizeOrderError error, HttpStatusCode? status, Exception inner)
    { if (error.TryGetError(out var value)) return ProviderError(status, value, inner); if (error.TryGetRawError(out var raw)) return ProviderError(raw.StatusCode, "PAYPAL_ERROR", "PayPal rejected the request.", null, inner); return ProviderError(status, null, inner); }
    private static PaymentWorkflowException Translate(GetAuthorizedPaymentError error, HttpStatusCode? status, Exception inner)
    { if (error.TryGetError(out var value)) return ProviderError(status, value, inner); if (error.TryGetNoContent(out var empty)) return ProviderError(empty.StatusCode, "PAYPAL_ERROR", "PayPal rejected the request.", null, inner); if (error.TryGetRawError(out var raw)) return ProviderError(raw.StatusCode, "PAYPAL_ERROR", "PayPal rejected the request.", null, inner); return ProviderError(status, null, inner); }
    private static PaymentWorkflowException Translate(ReauthorizePaymentError error, HttpStatusCode? status, Exception inner)
    { if (error.TryGetError(out var value)) return ProviderError(status, value, inner); if (error.TryGetNoContent(out var empty)) return ProviderError(empty.StatusCode, "PAYPAL_ERROR", "PayPal rejected the request.", null, inner); if (error.TryGetRawError(out var raw)) return ProviderError(raw.StatusCode, "PAYPAL_ERROR", "PayPal rejected the request.", null, inner); return ProviderError(status, null, inner); }
    private static PaymentWorkflowException Translate(CaptureAuthorizedPaymentError error, HttpStatusCode? status, Exception inner)
    { if (error.TryGetError(out var value)) return ProviderError(status, value, inner); if (error.TryGetNoContent(out var empty)) return ProviderError(empty.StatusCode, "PAYPAL_ERROR", "PayPal rejected the request.", null, inner); if (error.TryGetRawError(out var raw)) return ProviderError(raw.StatusCode, "PAYPAL_ERROR", "PayPal rejected the request.", null, inner); return ProviderError(status, null, inner); }
    private static PaymentWorkflowException Translate(GetCapturedPaymentError error, HttpStatusCode? status, Exception inner)
    { if (error.TryGetError(out var value)) return ProviderError(status, value, inner); if (error.TryGetNoContent(out var empty)) return ProviderError(empty.StatusCode, "PAYPAL_ERROR", "PayPal rejected the request.", null, inner); if (error.TryGetRawError(out var raw)) return ProviderError(raw.StatusCode, "PAYPAL_ERROR", "PayPal rejected the request.", null, inner); return ProviderError(status, null, inner); }
    private static PaymentWorkflowException Translate(VoidPaymentError error, HttpStatusCode? status, Exception inner)
    { if (error.TryGetError(out var value)) return ProviderError(status, value, inner); if (error.TryGetNoContent(out var empty)) return ProviderError(empty.StatusCode, "PAYPAL_ERROR", "PayPal rejected the request.", null, inner); if (error.TryGetRawError(out var raw)) return ProviderError(raw.StatusCode, "PAYPAL_ERROR", "PayPal rejected the request.", null, inner); return ProviderError(status, null, inner); }
    private static PaymentWorkflowException Translate(RefundCapturedPaymentError error, HttpStatusCode? status, Exception inner)
    { if (error.TryGetError(out var value)) return ProviderError(status, value, inner); if (error.TryGetNoContent(out var empty)) return ProviderError(empty.StatusCode, "PAYPAL_ERROR", "PayPal rejected the request.", null, inner); if (error.TryGetRawError(out var raw)) return ProviderError(raw.StatusCode, "PAYPAL_ERROR", "PayPal rejected the request.", null, inner); return ProviderError(status, null, inner); }
    private static PaymentWorkflowException Translate(GetRefundError error, HttpStatusCode? status, Exception inner)
    { if (error.TryGetError(out var value)) return ProviderError(status, value, inner); if (error.TryGetNoContent(out var empty)) return ProviderError(empty.StatusCode, "PAYPAL_ERROR", "PayPal rejected the request.", null, inner); if (error.TryGetRawError(out var raw)) return ProviderError(raw.StatusCode, "PAYPAL_ERROR", "PayPal rejected the request.", null, inner); return ProviderError(status, null, inner); }
    private static PaymentWorkflowException Translate(CreatePaymentTokenError error, HttpStatusCode? status, Exception inner)
    { if (error.TryGetError(out var value)) return ProviderError(status, value, inner); if (error.TryGetRawError(out var raw)) return ProviderError(raw.StatusCode, "PAYPAL_ERROR", "PayPal rejected the request.", null, inner); return ProviderError(status, null, inner); }
    private static PaymentWorkflowException Translate(DeletePaymentTokenError error, HttpStatusCode? status, Exception inner)
    { if (error.TryGetError(out var value)) return ProviderError(status, value, inner); if (error.TryGetRawError(out var raw)) return ProviderError(raw.StatusCode, "PAYPAL_ERROR", "PayPal rejected the request.", null, inner); return ProviderError(status, null, inner); }
}

public sealed record PayPalOrderResult(string OrderId, string? Status, PayPalAuthorizationResult? Authorization);
public sealed record PayPalAuthorizationResult(string AuthorizationId, string? Status, DateTimeOffset? CreatedAt, DateTimeOffset? ExpiresAt, decimal Amount);
public sealed record PayPalCaptureResult(string CaptureId, string? Status, decimal Amount, decimal? Fee, decimal? Net);
public sealed record PayPalRefundResult(string RefundId, string Status, decimal Amount);
public sealed record PayPalPaymentMethodResult(string VaultId, string Last4, string? Brand, string? Expiry, string? CustomerId);
