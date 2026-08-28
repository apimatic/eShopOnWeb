using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Enum;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PayPalPaymentGateway : IPayPalPaymentGateway
{
    private readonly PayPalServerSdkClient _client;
    private readonly PayPalSettings _settings;
    private static readonly TimeSpan TotalCallBudget = TimeSpan.FromSeconds(30);

    public PayPalPaymentGateway(PayPalServerSdkClient client, IOptions<PayPalSettings> settings)
    {
        _client = client;
        _settings = settings.Value;
    }

    public string Currency => _settings.Currency;

    public async Task<PayPalOrderResult> CreateOrderAsync(decimal amount, string invoiceId, string requestId, CancellationToken ct)
    {
        var response = await Run<PayPalServerSdk.Models.Order, CreateOrderError>(token => _client.Orders.CreateOrder(
            payPalMockResponse: null,
            payPalRequestId: requestId,
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
                        ReferenceId = "default",
                        InvoiceId = invoiceId,
                        CustomId = invoiceId,
                        Amount = new AmountWithBreakdown
                        {
                            CurrencyCode = Currency,
                            Value = MoneyValue(amount)
                        }
                    }
                }
            },
            prefer: "return=representation",
            requestOptions: null,
            ct: token), Convert, ct);

        return new PayPalOrderResult(Required(response.Id, "PayPal order id"), EnumValue(response.Status));
    }

    public async Task<AuthorizationResult> AuthorizeAsync(string payPalOrderId, decimal amount, CardInput? card,
        string? vaultedTokenId, string requestId, CancellationToken ct)
    {
        CardRequest cardRequest;
        if (card is not null)
        {
            cardRequest = new CardRequest
            {
                Name = card.Name,
                Number = card.Number,
                Expiry = card.Expiry,
                SecurityCode = card.SecurityCode,
                BillingAddress = Address(card.BillingAddress)
            };
        }
        else
        {
            cardRequest = new CardRequest
            {
                VaultId = vaultedTokenId,
                StoredCredential = new CardStoredCredential
                {
                    PaymentInitiator = PaymentInitiator.Customer,
                    PaymentType = StoredPaymentSourcePaymentType.OneTime,
                    Usage = StoredPaymentSourceUsageType.Subsequent
                }
            };
        }

        var response = await Run<OrderAuthorizeResponse, AuthorizeOrderError>(token => _client.Orders.AuthorizeOrder(
            id: payPalOrderId,
            payPalMockResponse: null,
            payPalRequestId: requestId,
            payPalClientMetadataId: null,
            payPalAuthAssertion: null,
            body: new OrderAuthorizeRequest
            {
                PaymentSource = new OrderAuthorizeRequestPaymentSource { Card = cardRequest }
            },
            prefer: "return=representation",
            requestOptions: null,
            ct: token), Convert, ct);

        var orderStatus = EnumValue(response.Status);
        if (response.Status == OrderStatus.PayerActionRequired)
        {
            return new AuthorizationResult(orderStatus, true, null, orderStatus, null, amount, null, null);
        }

        var authorization = response.PurchaseUnits?
            .SelectMany(p => p.Payments?.Authorizations ?? Array.Empty<AuthorizationWithAdditionalData>())
            .FirstOrDefault();
        if (authorization is null)
        {
            throw new PayPalProviderException(502, "PayPal did not return an authorization for the order.");
        }

        return Authorization(authorization, orderStatus, amount);
    }

    public async Task<AuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken ct)
    {
        var response = await Run<PaymentAuthorization, GetAuthorizedPaymentError>(token =>
            _client.Payments.GetAuthorizedPayment(authorizationId, null, null, null, token), Convert, ct);
        return Authorization(response, string.Empty, 0m);
    }

    public async Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string requestId, CancellationToken ct)
    {
        var response = await Run<PaymentAuthorization, ReauthorizePaymentError>(token => _client.Payments.ReauthorizePayment(
            authorizationId: authorizationId,
            payPalRequestId: requestId,
            payPalAuthAssertion: null,
            body: new ReauthorizeRequest { Amount = Money(amount) },
            prefer: "return=representation",
            requestOptions: null,
            ct: token), Convert, ct);
        return Authorization(response, string.Empty, amount);
    }

    public async Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string requestId, CancellationToken ct)
    {
        var response = await Run<CapturedPayment, CaptureAuthorizedPaymentError>(token =>
            _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: requestId,
                payPalAuthAssertion: null,
                body: new CaptureRequest { Amount = Money(amount), FinalCapture = true },
                prefer: "return=representation",
                requestOptions: null,
                ct: token), Convert, ct);
        return Capture(response, amount);
    }

    public async Task<CaptureResult> GetCaptureAsync(string captureId, CancellationToken ct)
    {
        var response = await Run<CapturedPayment, GetCapturedPaymentError>(token =>
            _client.Payments.GetCapturedPayment(captureId, null, null, token), Convert, ct);
        return Capture(response, 0m);
    }

    public async Task<AuthorizationResult> VoidAsync(string authorizationId, string requestId, CancellationToken ct)
    {
        var response = await Run<PaymentAuthorization, VoidPaymentError>(token => _client.Payments.VoidPayment(
            authorizationId: authorizationId,
            payPalMockResponse: null,
            payPalAuthAssertion: null,
            payPalRequestId: requestId,
            prefer: "return=representation",
            requestOptions: null,
            ct: token), Convert, ct);
        return Authorization(response, string.Empty, 0m);
    }

    public async Task<RefundResult> RefundAsync(string captureId, decimal? amount, string requestId, CancellationToken ct)
    {
        var body = amount.HasValue ? new RefundRequest { Amount = Money(amount.Value) } : new RefundRequest { };
        var response = await Run<Refund, RefundCapturedPaymentError>(token => _client.Payments.RefundCapturedPayment(
            captureId: captureId,
            payPalMockResponse: null,
            payPalRequestId: requestId,
            payPalAuthAssertion: null,
            body: body,
            prefer: "return=representation",
            requestOptions: null,
            ct: token), Convert, ct);
        return Refund(response, amount ?? 0m);
    }

    public async Task<RefundResult> GetRefundAsync(string refundId, CancellationToken ct)
    {
        var response = await Run<Refund, GetRefundError>(token =>
            _client.Payments.GetRefund(refundId, null, null, null, token), Convert, ct);
        return Refund(response, 0m);
    }

    public async Task<VaultedCardResult> SaveCardAsync(string merchantCustomerId, CardInput card, string requestId, CancellationToken ct)
    {
        var response = await Run<PaymentTokenResponse, CreatePaymentTokenError>(token => _client.Vault.CreatePaymentToken(
            payPalRequestId: requestId,
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
                        BillingAddress = Address(card.BillingAddress)
                    }
                }
            },
            requestOptions: null,
            ct: token), Convert, ct);

        var safeCard = response.PaymentSource?.Card;
        return new VaultedCardResult(
            Required(response.Id, "PayPal payment token id"),
            Required(response.Customer?.Id, "PayPal customer id"),
            safeCard?.Name,
            EnumValue(safeCard?.Brand),
            safeCard?.LastDigits,
            safeCard?.Expiry,
            EnumValue(safeCard?.Type));
    }

    public async Task<IReadOnlySet<string>> ListVaultedTokenIdsAsync(string customerId, CancellationToken ct)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var page = 1;
        var totalPages = 1;
        do
        {
            var response = await Run<CustomerVaultPaymentTokensResponse, ListCustomerPaymentTokensError>(token =>
                _client.Vault.ListCustomerPaymentTokens(customerId, 100, page, true, null, token), Convert, ct);
            foreach (var paymentToken in response.PaymentTokens ?? Array.Empty<PaymentTokenResponse>())
            {
                if (!string.IsNullOrWhiteSpace(paymentToken.Id)) result.Add(paymentToken.Id);
            }
            totalPages = Math.Max(1, response.TotalPages ?? 1);
            page++;
        } while (page <= totalPages);
        return result;
    }

    public async Task DeleteVaultedTokenAsync(string tokenId, CancellationToken ct)
    {
        await Run<bool, DeletePaymentTokenError>(async token =>
        {
            await _client.Vault.DeletePaymentToken(tokenId, null, token);
            return true;
        }, Convert, ct);
    }

    public async Task<IReadOnlyList<PayPalTransactionResult>> SearchTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var results = new List<PayPalTransactionResult>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var cursor = from.ToUniversalTime();
        var final = to.ToUniversalTime();

        while (cursor < final)
        {
            var windowEnd = cursor.AddDays(31) < final ? cursor.AddDays(31) : final;
            var page = 1;
            var totalPages = 1;
            do
            {
                SearchResponse response;
                try
                {
                    response = await Run<SearchResponse, RawError>(token => _client.TransactionSearch.SearchTransactions(
                        startDate: ReportDate(cursor),
                        endDate: ReportDate(windowEnd),
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
                        requestOptions: null,
                        ct: token), ConvertRaw, ct);
                }
                catch (PayPalProviderException ex) when (ReportingDataUnavailable(ex))
                {
                    break;
                }

                foreach (var detail in response.TransactionDetails ?? Array.Empty<TransactionDetails>())
                {
                    var info = detail.TransactionInfo;
                    if (info is null) continue;
                    var item = new PayPalTransactionResult(
                        info.TransactionId,
                        info.PaypalReferenceId,
                        info.PaypalReferenceIdType?.Value,
                        info.TransactionEventCode,
                        ParseDate(info.TransactionInitiationDate),
                        ParseDate(info.TransactionUpdatedDate),
                        ParseMoney(info.TransactionAmount),
                        ParseMoney(info.FeeAmount),
                        info.TransactionAmount?.CurrencyCode,
                        info.TransactionStatus,
                        info.InvoiceId,
                        info.CustomField,
                        info.InstrumentType);
                    var key = string.Join('|', item.TransactionId, item.PayPalReferenceId, item.EventCode,
                        item.InitiationDate?.ToString("O"), item.Amount?.ToString(CultureInfo.InvariantCulture));
                    if (seen.Add(key)) results.Add(item);
                }
                totalPages = Math.Max(1, response.TotalPages ?? 1);
                page++;
            } while (page <= totalPages);

            cursor = windowEnd;
        }

        return results;
    }

    private async Task<T> Run<T, TError>(Func<CancellationToken, Task<T>> call,
        Func<TError, PayPalProviderException> convert, CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TotalCallBudget);
        try
        {
            return await call(budget.Token);
        }
        catch (SdkException<TError> ex)
        {
            throw convert(ex.Error);
        }
        catch (JsonException ex)
        {
            throw new PayPalProviderException(502, "PayPal returned a response that could not be processed.", inner: ex);
        }
        catch (HttpRequestException ex)
        {
            throw new PayPalProviderException(503, "PayPal is currently unreachable.", inner: ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new PayPalProviderException(504, "The PayPal request timed out.", inner: ex);
        }
    }

    private static AuthorizationResult Authorization(AuthorizationWithAdditionalData value, string orderStatus, decimal fallbackAmount) =>
        new(orderStatus, false, value.Id, EnumValue(value.Status), EnumValue(value.StatusDetails?.Reason),
            ParseMoney(value.Amount) ?? fallbackAmount, ParseDate(value.CreateTime), ParseDate(value.ExpirationTime));

    private static AuthorizationResult Authorization(PaymentAuthorization value, string orderStatus, decimal fallbackAmount) =>
        new(orderStatus, false, value.Id, EnumValue(value.Status), EnumValue(value.StatusDetails?.Reason),
            ParseMoney(value.Amount) ?? fallbackAmount, ParseDate(value.CreateTime), ParseDate(value.ExpirationTime));

    private static CaptureResult Capture(CapturedPayment value, decimal fallbackAmount) => new(
        Required(value.Id, "PayPal capture id"), EnumValue(value.Status), EnumValue(value.StatusDetails?.Reason),
        ParseMoney(value.Amount) ?? fallbackAmount, ParseMoney(value.SellerReceivableBreakdown?.PaypalFee),
        ParseMoney(value.SellerReceivableBreakdown?.NetAmount), ParseDate(value.CreateTime));

    private static RefundResult Refund(Refund value, decimal fallbackAmount) => new(
        Required(value.Id, "PayPal refund id"), EnumValue(value.Status), EnumValue(value.StatusDetails?.Reason),
        ParseMoney(value.Amount) ?? fallbackAmount, ParseDate(value.UpdateTime ?? value.CreateTime));

    private Money Money(decimal amount) => new() { CurrencyCode = Currency, Value = MoneyValue(amount) };

    private PayPalServerSdk.Models.Address Address(CardBillingAddressInput address) => new()
    {
        AddressLine1 = address.AddressLine1,
        AddressLine2 = address.AddressLine2,
        AdminArea2 = address.AdminArea2,
        AdminArea1 = address.AdminArea1,
        PostalCode = address.PostalCode,
        CountryCode = address.CountryCode
    };

    private static string Required(string? value, string name) =>
        !string.IsNullOrWhiteSpace(value) ? value : throw new PayPalProviderException(502, $"{name} was missing from PayPal's response.");

    private static string MoneyValue(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    private static decimal? ParseMoney(Money? money) => money is null ? null : ParseDecimal(money.Value);
    private static decimal? ParseDecimal(string? value) => decimal.TryParse(value, NumberStyles.Number,
        CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    private static DateTimeOffset? ParseDate(string? value) => DateTimeOffset.TryParse(value,
        CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
        out var parsed) ? parsed : null;
    private static string ReportDate(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    private static bool ReportingDataUnavailable(PayPalProviderException error) =>
        error.StatusCode == 404 && error.Message.Contains("Data for the given start date is not available",
            StringComparison.OrdinalIgnoreCase);
    private static string EnumValue<TEnum>(StringEnum<TEnum>? value) where TEnum : StringEnum<TEnum> =>
        value?.Value ?? string.Empty;

    private static PayPalProviderException ConvertRaw(RawError error)
    {
        try
        {
            var typed = error.ReadAsJson<Error>();
            if (typed is not null)
            {
                return new PayPalProviderException((int)error.StatusCode,
                    ErrorMessage(typed.Message, typed.Details?.Select(detail =>
                        SafeDetail(detail.Issue, detail.Field, detail.Description)),
                        "PayPal rejected the request."), typed.DebugId);
            }
        }
        catch (JsonException)
        {
            // Raw Case-B payloads have no guaranteed schema; retain status and a safe generic message.
        }

        return new PayPalProviderException((int)error.StatusCode,
            "PayPal rejected the transaction-reporting request.");
    }

    private static PayPalProviderException Typed(Error error) => new(422,
        ErrorMessage(error.Message, error.Details?.Select(detail =>
            SafeDetail(detail.Issue, detail.Field, detail.Description)), "PayPal rejected the payment request."),
        error.DebugId);
    private static PayPalProviderException Typed(Error1 error) => new(422,
        ErrorMessage(error.Message, error.Details?.Select(detail =>
            SafeDetail(detail.Issue, detail.Field, detail.Description)), "PayPal rejected the vault request."),
        error.DebugId);

    private static string ErrorMessage(string? message, IEnumerable<string>? details, string fallback)
    {
        var summary = string.IsNullOrWhiteSpace(message) ? fallback : message;
        var safeDetails = details is null ? Array.Empty<string>() : details.Where(detail => detail.Length > 0).ToArray();
        return safeDetails.Length == 0 ? summary : $"{summary} Details: {string.Join("; ", safeDetails)}";
    }

    private static string SafeDetail(string issue, string? field, string? description)
    {
        var parts = new List<string> { $"issue={issue}" };
        if (!string.IsNullOrWhiteSpace(field)) parts.Add($"field={field}");
        if (!string.IsNullOrWhiteSpace(description)) parts.Add($"description={description}");
        return string.Join(", ", parts);
    }
    private static PayPalProviderException Unknown() => new(502, "PayPal returned an unrecognized error response.");

    private static PayPalProviderException Convert(CreateOrderError e) => e.TryGetError(out var x) ? Typed(x) : e.TryGetRawError(out var r) ? ConvertRaw(r) : Unknown();
    private static PayPalProviderException Convert(AuthorizeOrderError e) => e.TryGetError(out var x) ? Typed(x) : e.TryGetRawError(out var r) ? ConvertRaw(r) : Unknown();
    private static PayPalProviderException Convert(GetOrderError e) => e.TryGetError(out var x) ? Typed(x) : e.TryGetRawError(out var r) ? ConvertRaw(r) : Unknown();
    private static PayPalProviderException Convert(GetAuthorizedPaymentError e) => e.TryGetError(out var x) ? Typed(x) : e.TryGetNoContent(out var n) ? ConvertRaw(n) : e.TryGetRawError(out var r) ? ConvertRaw(r) : Unknown();
    private static PayPalProviderException Convert(ReauthorizePaymentError e) => e.TryGetError(out var x) ? Typed(x) : e.TryGetNoContent(out var n) ? ConvertRaw(n) : e.TryGetRawError(out var r) ? ConvertRaw(r) : Unknown();
    private static PayPalProviderException Convert(CaptureAuthorizedPaymentError e) => e.TryGetError(out var x) ? Typed(x) : e.TryGetNoContent(out var n) ? ConvertRaw(n) : e.TryGetRawError(out var r) ? ConvertRaw(r) : Unknown();
    private static PayPalProviderException Convert(GetCapturedPaymentError e) => e.TryGetError(out var x) ? Typed(x) : e.TryGetNoContent(out var n) ? ConvertRaw(n) : e.TryGetRawError(out var r) ? ConvertRaw(r) : Unknown();
    private static PayPalProviderException Convert(VoidPaymentError e) => e.TryGetError(out var x) ? Typed(x) : e.TryGetNoContent(out var n) ? ConvertRaw(n) : e.TryGetRawError(out var r) ? ConvertRaw(r) : Unknown();
    private static PayPalProviderException Convert(RefundCapturedPaymentError e) => e.TryGetError(out var x) ? Typed(x) : e.TryGetNoContent(out var n) ? ConvertRaw(n) : e.TryGetRawError(out var r) ? ConvertRaw(r) : Unknown();
    private static PayPalProviderException Convert(GetRefundError e) => e.TryGetError(out var x) ? Typed(x) : e.TryGetNoContent(out var n) ? ConvertRaw(n) : e.TryGetRawError(out var r) ? ConvertRaw(r) : Unknown();
    private static PayPalProviderException Convert(CreatePaymentTokenError e) => e.TryGetError1(out var x) ? Typed(x) : e.TryGetRawError(out var r) ? ConvertRaw(r) : Unknown();
    private static PayPalProviderException Convert(ListCustomerPaymentTokensError e) => e.TryGetError1(out var x) ? Typed(x) : e.TryGetRawError(out var r) ? ConvertRaw(r) : Unknown();
    private static PayPalProviderException Convert(DeletePaymentTokenError e) => e.TryGetError1(out var x) ? Typed(x) : e.TryGetRawError(out var r) ? ConvertRaw(r) : Unknown();
}
