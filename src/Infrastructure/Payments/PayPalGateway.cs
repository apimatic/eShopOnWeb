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
using PayPalCustomer = PayPalServerSdk.Models.Customer;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalGateway : IPayPalGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private readonly PayPalServerSdkClient _client;

    public PayPalGateway(PayPalServerSdkClient client)
    {
        _client = client;
    }

    public Task<AuthorizationResult> AuthorizeCardAsync(
        int orderId,
        decimal amount,
        string currency,
        string payPalRequestId,
        CardPaymentInput card,
        CancellationToken ct)
    {
        var cardRequest = ToCardRequest(card);
        return AuthorizeAsync(orderId, amount, currency, payPalRequestId, cardRequest, ct);
    }

    public Task<AuthorizationResult> AuthorizeSavedCardAsync(
        int orderId,
        decimal amount,
        string currency,
        string payPalRequestId,
        string vaultId,
        CancellationToken ct)
    {
        var cardRequest = new CardRequest { VaultId = vaultId };
        return AuthorizeAsync(orderId, amount, currency, payPalRequestId, cardRequest, ct);
    }

    public async Task<AuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken ct)
    {
        try
        {
            var auth = await Bounded(c => _client.Payments.GetAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                requestOptions: null,
                ct: c), ct);

            return MapAuthorization(auth.Id ?? authorizationId, auth.Status, auth.Amount, ParseTime(auth.CreateTime), ParseTime(auth.ExpirationTime), paypalOrderId: null);
        }
        catch (SdkException<GetAuthorizedPaymentError> ex)
        {
            throw Translate(ex.Error, 502);
        }
        catch (Exception ex) when (IsBoundary(ex))
        {
            throw TranslateBoundary(ex);
        }
    }

    public async Task<AuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string payPalRequestId,
        CancellationToken ct)
    {
        try
        {
            var auth = await Bounded(c => _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: payPalRequestId,
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest
                {
                    Amount = MoneyOf(currency, amount)
                },
                prefer: "return=representation",
                requestOptions: null,
                ct: c), ct);

            return MapAuthorization(auth.Id ?? authorizationId, auth.Status, auth.Amount, ParseTime(auth.CreateTime), ParseTime(auth.ExpirationTime), paypalOrderId: null);
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            throw Translate(ex.Error, 409);
        }
        catch (Exception ex) when (IsBoundary(ex))
        {
            throw TranslateBoundary(ex);
        }
    }

    public async Task<CaptureResult> CaptureAsync(string authorizationId, string payPalRequestId, CancellationToken ct)
    {
        try
        {
            var capture = await Bounded(c => _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: payPalRequestId,
                payPalAuthAssertion: null,
                body: new CaptureRequest { FinalCapture = true },
                prefer: "return=representation",
                requestOptions: null,
                ct: c), ct);

            return await MapCapture(capture, ct);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            throw Translate(ex.Error, 409);
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
            var capture = await Bounded(c => _client.Payments.GetCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                requestOptions: null,
                ct: c), ct);

            return await MapCapture(capture, ct);
        }
        catch (SdkException<GetCapturedPaymentError> ex)
        {
            throw Translate(ex.Error, 502);
        }
        catch (Exception ex) when (IsBoundary(ex))
        {
            throw TranslateBoundary(ex);
        }
    }

    public async Task VoidAsync(string authorizationId, string payPalRequestId, CancellationToken ct)
    {
        try
        {
            await Bounded(c => _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: payPalRequestId,
                prefer: "return=representation",
                requestOptions: null,
                ct: c), ct);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            throw Translate(ex.Error, 409);
        }
        catch (Exception ex) when (IsBoundary(ex))
        {
            throw TranslateBoundary(ex);
        }
    }

    public async Task<RefundResult> RefundAsync(
        string captureId,
        decimal? amount,
        string currency,
        string payPalRequestId,
        CancellationToken ct)
    {
        try
        {
            RefundRequest? body = amount is null
                ? null
                : new RefundRequest { Amount = MoneyOf(currency, amount.Value) };

            var refund = await Bounded(c => _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: payPalRequestId,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                requestOptions: null,
                ct: c), ct);

            return new RefundResult(
                refund.Id ?? string.Empty,
                refund.Status?.Value ?? string.Empty,
                ParseMoney(refund.Amount));
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            throw Translate(ex.Error, 409);
        }
        catch (Exception ex) when (IsBoundary(ex))
        {
            throw TranslateBoundary(ex);
        }
    }

    public async Task<VaultedCardResult> SaveCardAsync(string merchantCustomerId, CardPaymentInput card, string payPalRequestId, CancellationToken ct)
    {
        try
        {
            var body = new PaymentTokenRequest
            {
                Customer = new PayPalCustomer { MerchantCustomerId = merchantCustomerId },
                PaymentSource = new PaymentTokenRequestPaymentSource
                {
                    Card = ToVaultCard(card)
                }
            };

            var token = await Bounded(c => _client.Vault.CreatePaymentToken(
                payPalRequestId: payPalRequestId,
                body: body,
                requestOptions: null,
                ct: c), ct);

            var cardSource = token.PaymentSource?.Card;
            return new VaultedCardResult(
                token.Id ?? string.Empty,
                token.Customer?.Id,
                cardSource?.LastDigits,
                cardSource?.Brand?.Value,
                cardSource?.Expiry,
                cardSource?.Name);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            throw TranslateVault(ex.Error);
        }
        catch (Exception ex) when (IsBoundary(ex))
        {
            throw TranslateBoundary(ex);
        }
    }

    public async Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken ct)
    {
        try
        {
            await Bounded(c => _client.Vault.DeletePaymentToken(
                id: paymentTokenId,
                requestOptions: null,
                ct: c), ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            throw TranslateVault(ex.Error);
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
        var results = new List<PayPalTransactionRecord>();
        var cursor = from;
        while (cursor <= to)
        {
            var windowEnd = cursor.AddDays(31);
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            await SearchWindow(cursor, windowEnd, results, ct);

            if (windowEnd == to)
            {
                break;
            }

            cursor = windowEnd;
        }

        return results;
    }

    private async Task SearchWindow(DateTimeOffset start, DateTimeOffset end, List<PayPalTransactionRecord> sink, CancellationToken ct)
    {
        var page = 1;
        while (true)
        {
            SearchResponse response;
            try
            {
                response = await Bounded(c => _client.TransactionSearch.SearchTransactions(
                    startDate: Rfc3339(start),
                    endDate: Rfc3339(end),
                    transactionId: null,
                    transactionType: null,
                    transactionStatus: null,
                    transactionAmount: null,
                    transactionCurrency: null,
                    paymentInstrumentType: null,
                    storeId: null,
                    terminalId: null,
                    fields: "all",
                    balanceAffectingRecordsOnly: "Y",
                    pageSize: 100,
                    page: page,
                    requestOptions: null,
                    ct: c), ct);
            }
            catch (SdkException<RawError> ex)
            {
                throw FromRaw(ex.Error);
            }
            catch (Exception ex) when (IsBoundary(ex))
            {
                throw TranslateBoundary(ex);
            }

            var details = response.TransactionDetails;
            if (details != null)
            {
                foreach (var row in details)
                {
                    var info = row.TransactionInfo;
                    if (info == null)
                    {
                        continue;
                    }

                    sink.Add(new PayPalTransactionRecord(
                        info.TransactionId ?? string.Empty,
                        ParseMoney(info.TransactionAmount),
                        info.TransactionAmount?.CurrencyCode,
                        info.TransactionStatus,
                        ParseTime(info.TransactionInitiationDate),
                        info.PaypalReferenceId,
                        info.PaypalReferenceIdType?.Value,
                        info.InvoiceId,
                        info.CustomField,
                        info.TransactionEventCode,
                        ParseMoney(info.FeeAmount)));
                }
            }

            var pageSize = details?.Count ?? 0;
            if (response.TotalPages.HasValue)
            {
                if (page >= response.TotalPages.Value)
                {
                    break;
                }
            }
            else if (pageSize < 100)
            {
                break;
            }

            page++;
        }
    }

    private async Task<AuthorizationResult> AuthorizeAsync(
        int orderId,
        decimal amount,
        string currency,
        string payPalRequestId,
        CardRequest cardRequest,
        CancellationToken ct)
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
                        Value = FormatMoney(amount)
                    },
                    CustomId = orderId.ToString(CultureInfo.InvariantCulture),
                    InvoiceId = payPalRequestId
                }
            },
            PaymentSource = new PaymentSource
            {
                Card = cardRequest
            }
        };

        try
        {
            var created = await Bounded(c => _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: payPalRequestId,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                requestOptions: null,
                ct: c), ct);

            RefusePayerAction(created.Status, created.Links);

            var authorization = FirstAuthorization(created.PurchaseUnits);
            var paypalOrderId = created.Id;
            if (authorization == null)
            {
                var authorized = await Bounded(c => _client.Orders.AuthorizeOrder(
                    id: created.Id ?? string.Empty,
                    payPalMockResponse: null,
                    payPalRequestId: payPalRequestId + "-authorize",
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: new OrderAuthorizeRequest
                    {
                        PaymentSource = new OrderAuthorizeRequestPaymentSource { Card = cardRequest }
                    },
                    prefer: "return=representation",
                    requestOptions: null,
                    ct: c), ct);

                RefusePayerAction(authorized.Status, authorized.Links);
                authorization = FirstAuthorization(authorized.PurchaseUnits);
                paypalOrderId = authorized.Id ?? created.Id;
            }

            if (authorization == null || string.IsNullOrEmpty(authorization.Id))
            {
                throw new CheckoutException(502, "PayPal authorized the order but did not return an authorization id.");
            }

            return MapAuthorization(
                authorization.Id,
                authorization.Status,
                authorization.Amount,
                ParseTime(authorization.CreateTime),
                ParseTime(authorization.ExpirationTime),
                paypalOrderId ?? string.Empty);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw Translate(ex.Error, 400);
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            throw Translate(ex.Error, 400);
        }
        catch (Exception ex) when (IsBoundary(ex))
        {
            throw TranslateBoundary(ex);
        }
    }

    private async Task<CaptureResult> MapCapture(CapturedPayment capture, CancellationToken ct)
    {
        var breakdown = capture.SellerReceivableBreakdown;
        if (breakdown == null && !string.IsNullOrEmpty(capture.Id))
        {
            var fresh = await Bounded(c => _client.Payments.GetCapturedPayment(
                captureId: capture.Id,
                payPalMockResponse: null,
                requestOptions: null,
                ct: c), ct);
            capture = fresh;
            breakdown = capture.SellerReceivableBreakdown;
        }

        return new CaptureResult(
            capture.Id ?? string.Empty,
            capture.Status?.Value ?? string.Empty,
            ParseMoney(capture.Amount),
            ParseNullableMoney(breakdown?.PaypalFee),
            ParseNullableMoney(breakdown?.NetAmount));
    }

    private static AuthorizationWithAdditionalData? FirstAuthorization(IReadOnlyList<PurchaseUnit>? purchaseUnits) =>
        purchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();

    private static void RefusePayerAction(OrderStatus? status, IReadOnlyList<LinkDescription>? links)
    {
        if (status == OrderStatus.PayerActionRequired)
        {
            throw PayerActionRequired();
        }

        if (links != null && links.Any(l => string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase)))
        {
            throw PayerActionRequired();
        }
    }

    private static CheckoutException PayerActionRequired() =>
        new(422, "PayPal required a browser challenge (3-D Secure / payer-action) to complete this card payment. This integration does not support a shopper approval round-trip. Use a card that completes without a challenge.");

    private static AuthorizationResult MapAuthorization(
        string authorizationId,
        AuthorizationStatus? status,
        Money? amount,
        DateTimeOffset? createTime,
        DateTimeOffset? expirationTime,
        string? paypalOrderId)
    {
        return new AuthorizationResult(
            paypalOrderId ?? string.Empty,
            authorizationId,
            status?.Value ?? string.Empty,
            ParseMoney(amount),
            createTime,
            expirationTime);
    }

    private static CardRequest ToCardRequest(CardPaymentInput card) =>
        new()
        {
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            Name = card.Name,
            BillingAddress = ToAddress(card.BillingAddress)
        };

    private static PaymentTokenRequestCard ToVaultCard(CardPaymentInput card) =>
        new()
        {
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            Name = card.Name,
            BillingAddress = ToAddress(card.BillingAddress)
        };

    private static Address? ToAddress(BillingAddressInput? address)
    {
        if (address == null)
        {
            return null;
        }

        return new Address
        {
            AddressLine1 = address.AddressLine1,
            AddressLine2 = address.AddressLine2,
            AdminArea2 = address.AdminArea2,
            AdminArea1 = address.AdminArea1,
            PostalCode = address.PostalCode,
            CountryCode = address.CountryCode
        };
    }

    private static Money MoneyOf(string currency, decimal amount) =>
        new()
        {
            CurrencyCode = currency,
            Value = FormatMoney(amount)
        };

    private static string FormatMoney(decimal amount) =>
        amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal ParseMoney(Money? money) =>
        ParseNullableMoney(money) ?? 0m;

    private static decimal? ParseNullableMoney(Money? money)
    {
        if (money?.Value == null)
        {
            return null;
        }

        if (decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        return null;
    }

    private static DateTimeOffset? ParseTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string Rfc3339(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private async Task Bounded(Func<CancellationToken, Task> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        await call(cts.Token);
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private static bool IsBoundary(Exception ex) =>
        ex is JsonException or HttpRequestException or TaskCanceledException or OperationCanceledException;

    private static CheckoutException TranslateBoundary(Exception ex) =>
        ex is JsonException
            ? new CheckoutException(502, "The payment provider returned a response that could not be processed.", ex)
            : new CheckoutException(503, "The payment provider is unreachable.", ex);

    private static CheckoutException Translate(CreateOrderError error, int fallback) =>
        TranslateCaseA(error, fallback, e => e.TryGetError(out var body) ? body : null, e => e.TryGetRawError(out var raw) ? raw : null);

    private static CheckoutException Translate(AuthorizeOrderError error, int fallback) =>
        TranslateCaseA(error, fallback, e => e.TryGetError(out var body) ? body : null, e => e.TryGetRawError(out var raw) ? raw : null);

    private static CheckoutException Translate(CaptureAuthorizedPaymentError error, int fallback)
    {
        if (error.TryGetError(out var body))
        {
            return FromError(body, fallback);
        }

        if (error.TryGetNoContent(out var noContent))
        {
            return FromRaw(noContent);
        }

        if (error.TryGetRawError(out var raw))
        {
            return FromRaw(raw);
        }

        return new CheckoutException(fallback, "PayPal rejected the capture.");
    }

    private static CheckoutException Translate(GetCapturedPaymentError error, int fallback)
    {
        if (error.TryGetError(out var body))
        {
            return FromError(body, fallback);
        }

        if (error.TryGetNoContent(out var noContent))
        {
            return FromRaw(noContent);
        }

        if (error.TryGetRawError(out var raw))
        {
            return FromRaw(raw);
        }

        return new CheckoutException(fallback, "PayPal could not return the capture.");
    }

    private static CheckoutException Translate(GetAuthorizedPaymentError error, int fallback)
    {
        if (error.TryGetError(out var body))
        {
            return FromError(body, fallback);
        }

        if (error.TryGetNoContent(out var noContent))
        {
            return FromRaw(noContent);
        }

        if (error.TryGetRawError(out var raw))
        {
            return FromRaw(raw);
        }

        return new CheckoutException(fallback, "PayPal could not return the authorization.");
    }

    private static CheckoutException Translate(ReauthorizePaymentError error, int fallback)
    {
        if (error.TryGetError(out var body))
        {
            return FromError(body, fallback);
        }

        if (error.TryGetNoContent(out var noContent))
        {
            return FromRaw(noContent);
        }

        if (error.TryGetRawError(out var raw))
        {
            return FromRaw(raw);
        }

        return new CheckoutException(fallback, "PayPal could not renew the authorization.");
    }

    private static CheckoutException Translate(VoidPaymentError error, int fallback)
    {
        if (error.TryGetError(out var body))
        {
            return FromError(body, fallback);
        }

        if (error.TryGetNoContent(out var noContent))
        {
            return FromRaw(noContent);
        }

        if (error.TryGetRawError(out var raw))
        {
            return FromRaw(raw);
        }

        return new CheckoutException(fallback, "PayPal could not release the authorization.");
    }

    private static CheckoutException Translate(RefundCapturedPaymentError error, int fallback)
    {
        if (error.TryGetError(out var body))
        {
            return FromError(body, fallback);
        }

        if (error.TryGetNoContent(out var noContent))
        {
            return FromRaw(noContent);
        }

        if (error.TryGetRawError(out var raw))
        {
            return FromRaw(raw);
        }

        return new CheckoutException(fallback, "PayPal rejected the refund.");
    }

    private static CheckoutException TranslateVault(CreatePaymentTokenError error)
    {
        if (error.TryGetError1(out var body))
        {
            return FromError1(body);
        }

        if (error.TryGetRawError(out var raw))
        {
            return FromRaw(raw);
        }

        return new CheckoutException(400, "PayPal rejected the saved card.");
    }

    private static CheckoutException TranslateVault(DeletePaymentTokenError error)
    {
        if (error.TryGetError1(out var body))
        {
            return FromError1(body);
        }

        if (error.TryGetRawError(out var raw))
        {
            return FromRaw(raw);
        }

        return new CheckoutException(400, "PayPal could not delete the saved card.");
    }

    private static CheckoutException TranslateCaseA<TError>(
        TError error,
        int fallback,
        Func<TError, Error?> typed,
        Func<TError, RawError?> raw)
    {
        var body = typed(error);
        if (body != null)
        {
            return FromError(body, fallback);
        }

        var rawError = raw(error);
        if (rawError != null)
        {
            return FromRaw(rawError);
        }

        return new CheckoutException(fallback, "PayPal rejected the request.");
    }

    private static CheckoutException FromError(Error error, int fallback)
    {
        var status = error.Name switch
        {
            "AUTHENTICATION_FAILURE" => 502,
            "NOT_AUTHORIZED" => 502,
            "RESOURCE_NOT_FOUND" => 404,
            "RESOURCE_CONFLICT" => 409,
            "UNPROCESSABLE_ENTITY" => 422,
            "INVALID_REQUEST" => 400,
            _ => fallback
        };

        return new CheckoutException(status, Describe(error));
    }

    private static CheckoutException FromError1(Error1 error)
    {
        var status = error.Name switch
        {
            "AUTHENTICATION_FAILURE" => 502,
            "NOT_AUTHORIZED" => 502,
            "RESOURCE_NOT_FOUND" => 404,
            "RESOURCE_CONFLICT" => 409,
            "UNPROCESSABLE_ENTITY" => 422,
            "INVALID_REQUEST" => 400,
            _ => 400
        };

        return new CheckoutException(status, Describe(error));
    }

    private static CheckoutException FromRaw(RawError raw)
    {
        var status = (int)raw.StatusCode;
        if (status < 400)
        {
            status = 502;
        }

        var body = raw.ReadAsString();
        if (string.IsNullOrWhiteSpace(body))
        {
            return new CheckoutException(status, $"PayPal returned HTTP {status}.");
        }

        var trimmed = body.Length > 800 ? body.Substring(0, 800) : body;
        return new CheckoutException(status, $"PayPal returned HTTP {status}: {trimmed}");
    }

    private static string Describe(Error error)
    {
        var issues = error.Details == null
            ? string.Empty
            : string.Join("; ", error.Details.Select(d => string.IsNullOrEmpty(d.Description) ? d.Issue : $"{d.Issue}: {d.Description}"));

        var suffix = string.IsNullOrEmpty(issues) ? error.Message : $"{error.Message} ({issues})";
        return string.IsNullOrEmpty(error.DebugId) ? $"{error.Name}: {suffix}" : $"{error.Name}: {suffix} [debug {error.DebugId}]";
    }

    private static string Describe(Error1 error)
    {
        var issues = error.Details == null
            ? string.Empty
            : string.Join("; ", error.Details.Select(d => string.IsNullOrEmpty(d.Description) ? d.Issue : $"{d.Issue}: {d.Description}"));

        var suffix = string.IsNullOrEmpty(issues) ? error.Message : $"{error.Message} ({issues})";
        return string.IsNullOrEmpty(error.DebugId) ? $"{error.Name}: {suffix}" : $"{error.Name}: {suffix} [debug {error.DebugId}]";
    }
}
