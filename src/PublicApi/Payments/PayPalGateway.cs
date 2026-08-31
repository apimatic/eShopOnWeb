using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
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

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PayPalGateway : IPayPalGateway
{
    private readonly PayPalServerSdkClient _client;
    private readonly TimeSpan _callBudget = TimeSpan.FromSeconds(30);

    public PayPalGateway(PayPalServerSdkClient client)
    {
        _client = client;
    }

    public async Task<ProviderAuthorization> AuthorizeAsync(int orderId, string operationId, decimal amount,
        string currency, CardInput? card, string? vaultId, CancellationToken cancellationToken)
    {
        var money = Money(amount, currency);
        PayPalServerSdk.Models.Order order;
        try
        {
            order = await Bounded(ct => _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: operationId + "-order",
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
                            Amount = new AmountWithBreakdown { CurrencyCode = currency, Value = Format(amount) },
                            InvoiceId = InvoiceId(orderId, operationId),
                            CustomId = orderId.ToString(CultureInfo.InvariantCulture)
                        }
                    }
                },
                prefer: "return=representation",
                requestOptions: null,
                ct: ct), cancellationToken);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw FromCreateOrder(ex);
        }
        catch (Exception ex) when (IsBoundaryFailure(ex))
        {
            throw FromBoundary(ex, true);
        }

        var cardRequest = vaultId is not null
            ? new CardRequest
            {
                VaultId = vaultId,
                StoredCredential = new CardStoredCredential
                {
                    PaymentInitiator = PaymentInitiator.Customer,
                    PaymentType = StoredPaymentSourcePaymentType.OneTime,
                    Usage = StoredPaymentSourceUsageType.Subsequent
                }
            }
            : ToCardRequest(card!);

        OrderAuthorizeResponse authorized;
        try
        {
            authorized = await Bounded(ct => _client.Orders.AuthorizeOrder(
                id: order.Id,
                payPalMockResponse: null,
                payPalRequestId: operationId + "-authorize",
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: new OrderAuthorizeRequest
                {
                    PaymentSource = new OrderAuthorizeRequestPaymentSource { Card = cardRequest }
                },
                prefer: "return=representation",
                requestOptions: null,
                ct: ct), cancellationToken);
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            throw FromAuthorizeOrder(ex);
        }
        catch (Exception ex) when (IsBoundaryFailure(ex))
        {
            throw FromBoundary(ex, true);
        }

        if (authorized.Status == OrderStatus.PayerActionRequired)
        {
            throw new PayPalProviderException(
                "PayPal requires browser approval for this card. No authorization was completed.", 409);
        }

        var authorization = authorized.PurchaseUnits?
            .SelectMany(x => x.Payments?.Authorizations ?? Array.Empty<AuthorizationWithAdditionalData>())
            .SingleOrDefault() ?? throw new PayPalProviderException(
                "PayPal did not return the authorization that was created.", outcomeUnknown: true);

        return new ProviderAuthorization(
            authorized.Id,
            authorized.Status.Value,
            authorization.Id,
            authorization.Status.Value,
            Parse(authorization.Amount.Value),
            authorization.Amount.CurrencyCode,
            ParseTimestamp(authorization.CreateTime, DateTimeOffset.UtcNow),
            ParseNullableTimestamp(authorization.ExpirationTime));
    }

    public async Task<ProviderAuthorization> GetAuthorizationAsync(string authorizationId, string paypalOrderId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await Bounded(ct => _client.Payments.GetAuthorizedPayment(
                authorizationId, null, null, null, ct), cancellationToken);
            return Authorization(result, paypalOrderId, string.Empty);
        }
        catch (SdkException<GetAuthorizedPaymentError> ex)
        {
            throw FromGetAuthorization(ex);
        }
        catch (Exception ex) when (IsBoundaryFailure(ex))
        {
            throw FromBoundary(ex, false);
        }
    }

    public async Task<ProviderAuthorization> ReauthorizeAsync(string authorizationId, string paypalOrderId,
        string requestId, decimal amount, string currency, CancellationToken cancellationToken)
    {
        try
        {
            var result = await Bounded(ct => _client.Payments.ReauthorizePayment(
                authorizationId, requestId, null,
                new ReauthorizeRequest { Amount = Money(amount, currency) },
                "return=representation", null, ct), cancellationToken);
            return Authorization(result, paypalOrderId, currency);
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            throw FromReauthorize(ex);
        }
        catch (Exception ex) when (IsBoundaryFailure(ex))
        {
            throw FromBoundary(ex, true);
        }
    }

    public async Task<ProviderCapture> CaptureAsync(string authorizationId, string requestId, int orderId,
        decimal amount, string currency, CancellationToken cancellationToken)
    {
        try
        {
            var result = await Bounded(ct => _client.Payments.CaptureAuthorizedPayment(
                authorizationId, null, requestId, null,
                new CaptureRequest
                {
                    Amount = Money(amount, currency),
                    FinalCapture = true
                },
                "return=representation", null, ct), cancellationToken);
            return Capture(result);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            throw FromCapture(ex);
        }
        catch (Exception ex) when (IsBoundaryFailure(ex))
        {
            throw FromBoundary(ex, true);
        }
    }

    public async Task<ProviderCapture> GetCaptureAsync(string captureId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await Bounded(ct => _client.Payments.GetCapturedPayment(captureId, null, null, ct),
                cancellationToken);
            return Capture(result);
        }
        catch (SdkException<GetCapturedPaymentError> ex)
        {
            throw FromGetCapture(ex);
        }
        catch (Exception ex) when (IsBoundaryFailure(ex))
        {
            throw FromBoundary(ex, false);
        }
    }

    public async Task<string> VoidAsync(string authorizationId, string requestId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await Bounded(ct => _client.Payments.VoidPayment(
                authorizationId, null, null, requestId, "return=representation", null, ct), cancellationToken);
            return result.Status.Value;
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            throw FromVoid(ex);
        }
        catch (Exception ex) when (IsBoundaryFailure(ex))
        {
            throw FromBoundary(ex, true);
        }
    }

    public async Task<ProviderRefund> RefundAsync(string captureId, string idempotencyKey, decimal? amount,
        string currency, CancellationToken cancellationToken)
    {
        try
        {
            var body = new RefundRequest { Amount = amount.HasValue ? Money(amount.Value, currency) : null };
            var result = await Bounded(ct => _client.Payments.RefundCapturedPayment(
                captureId, null, idempotencyKey, null, body, "return=representation", null, ct),
                cancellationToken);
            return Refund(result);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            throw FromRefund(ex);
        }
        catch (Exception ex) when (IsBoundaryFailure(ex))
        {
            throw FromBoundary(ex, true);
        }
    }

    public async Task<ProviderRefund> GetRefundAsync(string refundId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await Bounded(ct => _client.Payments.GetRefund(refundId, null, null, null, ct),
                cancellationToken);
            return Refund(result);
        }
        catch (SdkException<GetRefundError> ex)
        {
            throw FromGetRefund(ex);
        }
        catch (Exception ex) when (IsBoundaryFailure(ex))
        {
            throw FromBoundary(ex, false);
        }
    }

    public async Task<ProviderSavedCard> SaveCardAsync(string ownerId, string requestId, CardInput card,
        string? existingCustomerId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await Bounded(ct => _client.Vault.CreatePaymentToken(
                requestId,
                new PaymentTokenRequest
                {
                    Customer = new Customer { Id = existingCustomerId, MerchantCustomerId = ownerId },
                    PaymentSource = new PaymentTokenRequestPaymentSource
                    {
                        Card = new PaymentTokenRequestCard
                        {
                            Name = card.Name,
                            Number = card.Number,
                            Expiry = card.Expiry,
                            SecurityCode = card.SecurityCode,
                            BillingAddress = ToAddress(card.BillingAddress)
                        }
                    }
                }, null, ct), cancellationToken);
            var saved = result.PaymentSource.Card;
            return new ProviderSavedCard(result.Id, result.Customer.Id, saved.Brand?.Value,
                saved.LastDigits, saved.Expiry, saved.Type?.Value);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            throw FromCreateToken(ex);
        }
        catch (Exception ex) when (IsBoundaryFailure(ex))
        {
            throw FromBoundary(ex, true);
        }
    }

    public async Task<IReadOnlyList<ProviderSavedCard>> ListCardsAsync(string customerId,
        CancellationToken cancellationToken)
    {
        var cards = new List<ProviderSavedCard>();
        var page = 1;
        try
        {
            while (true)
            {
                var result = await Bounded(ct => _client.Vault.ListCustomerPaymentTokens(
                    customerId, pageSize: 100, page: page, totalRequired: true, requestOptions: null, ct: ct),
                    cancellationToken);
                foreach (var token in result.PaymentTokens ?? Array.Empty<PaymentTokenResponse>())
                {
                    var card = token.PaymentSource.Card;
                    cards.Add(new ProviderSavedCard(token.Id, result.Customer.Id, card.Brand?.Value,
                        card.LastDigits, card.Expiry, card.Type?.Value));
                }

                if (page >= (result.TotalPages ?? page) || result.PaymentTokens is null || result.PaymentTokens.Count == 0)
                {
                    break;
                }
                page++;
            }
            return cards;
        }
        catch (SdkException<ListCustomerPaymentTokensError> ex)
        {
            throw FromListTokens(ex);
        }
        catch (Exception ex) when (IsBoundaryFailure(ex))
        {
            throw FromBoundary(ex, false);
        }
    }

    public async Task DeleteCardAsync(string tokenId, CancellationToken cancellationToken)
    {
        try
        {
            await Bounded(async ct =>
            {
                await _client.Vault.DeletePaymentToken(tokenId, null, ct);
                return true;
            }, cancellationToken);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            throw FromDeleteToken(ex);
        }
        catch (Exception ex) when (IsBoundaryFailure(ex))
        {
            throw FromBoundary(ex, true);
        }
    }

    public async Task<IReadOnlyList<ProviderTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken)
    {
        var transactions = new List<ProviderTransaction>();
        var now = ReportSecond(DateTimeOffset.UtcNow);
        var rangeStart = ReportSecond(from.ToUniversalTime());
        var earliestAvailable = ReportSecond(now.AddYears(-3).AddSeconds(1));
        if (rangeStart < earliestAvailable)
        {
            rangeStart = earliestAvailable;
        }
        var rangeEnd = ReportSecond(to.ToUniversalTime() > now ? now : to.ToUniversalTime());
        if (rangeStart > rangeEnd)
        {
            return transactions;
        }

        try
        {
            while (rangeStart <= rangeEnd)
            {
                var maximumWindowEnd = rangeStart.AddDays(31).AddSeconds(-1);
                var windowEnd = maximumWindowEnd < rangeEnd ? maximumWindowEnd : rangeEnd;
                var page = 1;
                while (true)
                {
                    var result = await Bounded(ct => _client.TransactionSearch.SearchTransactions(
                        startDate: ReportTimestamp(rangeStart),
                        endDate: ReportTimestamp(windowEnd),
                        transactionId: null, transactionType: null, transactionStatus: null,
                        transactionAmount: null, transactionCurrency: null, paymentInstrumentType: null,
                        storeId: null, terminalId: null, fields: "transaction_info",
                        balanceAffectingRecordsOnly: "Y", pageSize: 100, page: page,
                        requestOptions: null, ct: ct), cancellationToken);
                    foreach (var detail in result.TransactionDetails ?? Array.Empty<TransactionDetails>())
                    {
                        var info = detail.TransactionInfo;
                        transactions.Add(new ProviderTransaction(info.TransactionId, info.PaypalReferenceId,
                            info.InvoiceId, info.CustomField, info.TransactionStatus, info.TransactionEventCode,
                            info.TransactionAmount is null ? null : Parse(info.TransactionAmount.Value),
                            info.TransactionAmount?.CurrencyCode,
                            info.FeeAmount is null ? null : Parse(info.FeeAmount.Value),
                            ParseNullableTimestamp(info.TransactionInitiationDate),
                            ParseNullableTimestamp(info.TransactionUpdatedDate)));
                    }
                    if (page >= (result.TotalPages ?? page) || result.TransactionDetails is null || result.TransactionDetails.Count == 0)
                    {
                        break;
                    }
                    page++;
                }
                rangeStart = windowEnd.AddSeconds(1);
            }
            return transactions;
        }
        catch (SdkException<RawError> ex)
        {
            throw ReportingRaw(ex.Error);
        }
        catch (Exception ex) when (IsBoundaryFailure(ex))
        {
            throw FromBoundary(ex, false);
        }
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(_callBudget);
        return await call(source.Token);
    }

    private static ProviderAuthorization Authorization(PaymentAuthorization value, string orderId, string fallbackCurrency) =>
        new(orderId, string.Empty, value.Id, value.Status.Value, Parse(value.Amount.Value),
            value.Amount.CurrencyCode ?? fallbackCurrency,
            ParseTimestamp(value.CreateTime, DateTimeOffset.UtcNow),
            ParseNullableTimestamp(value.ExpirationTime));

    private static ProviderCapture Capture(CapturedPayment value) => new(
        value.Id, value.Status.Value, Parse(value.Amount.Value), value.Amount.CurrencyCode,
        value.SellerReceivableBreakdown?.PaypalFee is null ? null : Parse(value.SellerReceivableBreakdown.PaypalFee.Value),
        value.SellerReceivableBreakdown?.NetAmount is null ? null : Parse(value.SellerReceivableBreakdown.NetAmount.Value),
        ParseNullableTimestamp(value.UpdateTime));

    private static ProviderRefund Refund(PayPalServerSdk.Models.Refund value) => new(
        value.Id, value.Status.Value, Parse(value.Amount.Value), value.Amount.CurrencyCode,
        ParseNullableTimestamp(value.UpdateTime));

    private static CardRequest ToCardRequest(CardInput card) => new()
    {
        Name = card.Name,
        Number = card.Number,
        Expiry = card.Expiry,
        SecurityCode = card.SecurityCode,
        BillingAddress = ToAddress(card.BillingAddress)
    };

    private static Address ToAddress(CardBillingAddress address) => new()
    {
        CountryCode = address.CountryCode,
        AddressLine1 = address.AddressLine1,
        AddressLine2 = address.AddressLine2,
        AdminArea1 = address.AdminArea1,
        AdminArea2 = address.AdminArea2,
        PostalCode = address.PostalCode
    };

    private static Money Money(decimal amount, string currency) =>
        new() { CurrencyCode = currency, Value = Format(amount) };

    private static string Format(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    private static decimal Parse(string value) => decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
    private static DateTimeOffset ReportSecond(DateTimeOffset value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second, TimeSpan.Zero);
    private static string ReportTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    private static string InvoiceId(int orderId, string operationId)
    {
        var value = $"eshop-order-{orderId}-{operationId}";
        return value.Length <= 127 ? value : value[..127];
    }

    private static DateTimeOffset ParseTimestamp(string? value, DateTimeOffset fallback) =>
        value is null ? fallback : DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    private static DateTimeOffset? ParseNullableTimestamp(string? value) =>
        value is null ? null : DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    private static bool IsBoundaryFailure(Exception ex) => ex is HttpRequestException or TaskCanceledException or JsonException;
    private static PayPalProviderException FromBoundary(Exception ex, bool write) => new(
        ex is JsonException ? "PayPal returned a response that could not be processed." : "PayPal is currently unreachable.",
        outcomeUnknown: write, innerException: ex);

    private static PayPalProviderException Typed(Error error) => new(
        TypedMessage(error.Name, error.Message, error.Details?.Select(detail =>
            SafeDetail(detail.Issue, detail.Description, detail.Field))), debugId: error.DebugId);
    private static PayPalProviderException Typed(Error1 error) => new(
        TypedMessage(error.Name, error.Message, error.Details?.Select(detail =>
            SafeDetail(detail.Issue, detail.Description, detail.Field))), debugId: error.DebugId);
    private static string TypedMessage(string name, string message, IEnumerable<string>? details)
    {
        var safeDetails = details?.Where(detail => detail.Length > 0).ToArray();
        return safeDetails is { Length: > 0 }
            ? $"{name}: {message} Details: {string.Join("; ", safeDetails)}"
            : $"{name}: {message}";
    }

    private static string SafeDetail(string issue, string? description, string? field)
    {
        var parts = new[]
        {
            $"issue={issue}",
            description is null ? null : $"description={description}",
            field is null ? null : $"field={field}"
        };
        return string.Join(", ", parts.Where(part => part is not null));
    }
    private static PayPalProviderException Raw(RawError raw) => new(
        "PayPal rejected the operation.", (int)raw.StatusCode);
    private static PayPalProviderException ReportingRaw(RawError raw)
    {
        try
        {
            using var document = JsonDocument.Parse(raw.ReadAsString());
            var root = document.RootElement;
            var name = JsonString(root, "name") ?? "REPORTING_REQUEST_REJECTED";
            var message = JsonString(root, "message") ?? "PayPal rejected the reporting request.";
            var debugId = JsonString(root, "debug_id");
            var details = new List<string>();
            if (root.TryGetProperty("details", out var detailArray) && detailArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in detailArray.EnumerateArray())
                {
                    details.Add(SafeDetail(
                        JsonString(detail, "issue") ?? "UNKNOWN",
                        JsonString(detail, "description"),
                        JsonString(detail, "field")));
                }
            }
            return new PayPalProviderException(TypedMessage(name, message, details),
                (int)raw.StatusCode, debugId: debugId);
        }
        catch (JsonException)
        {
            return new PayPalProviderException("PayPal rejected the reporting request.", (int)raw.StatusCode);
        }
    }

    private static string? JsonString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static PayPalProviderException FromCreateOrder(SdkException<CreateOrderError> ex) =>
        ex.Error.TryGetError(out var e) ? Typed(e) : ex.Error.TryGetRawError(out var r) ? Raw(r) : new("PayPal rejected order creation.");
    private static PayPalProviderException FromAuthorizeOrder(SdkException<AuthorizeOrderError> ex) =>
        ex.Error.TryGetError(out var e) ? Typed(e) : ex.Error.TryGetRawError(out var r) ? Raw(r) : new("PayPal rejected authorization.");
    private static PayPalProviderException FromGetAuthorization(SdkException<GetAuthorizedPaymentError> ex) =>
        ex.Error.TryGetError(out var e) ? Typed(e) : ex.Error.TryGetNoContent(out var n) ? Raw(n) : ex.Error.TryGetRawError(out var r) ? Raw(r) : new("PayPal could not read the authorization.");
    private static PayPalProviderException FromReauthorize(SdkException<ReauthorizePaymentError> ex) =>
        ex.Error.TryGetError(out var e) ? Typed(e) : ex.Error.TryGetNoContent(out var n) ? Raw(n) : ex.Error.TryGetRawError(out var r) ? Raw(r) : new("PayPal rejected reauthorization.");
    private static PayPalProviderException FromCapture(SdkException<CaptureAuthorizedPaymentError> ex) =>
        ex.Error.TryGetError(out var e) ? Typed(e) : ex.Error.TryGetNoContent(out var n) ? Raw(n) : ex.Error.TryGetRawError(out var r) ? Raw(r) : new("PayPal rejected capture.");
    private static PayPalProviderException FromGetCapture(SdkException<GetCapturedPaymentError> ex) =>
        ex.Error.TryGetError(out var e) ? Typed(e) : ex.Error.TryGetNoContent(out var n) ? Raw(n) : ex.Error.TryGetRawError(out var r) ? Raw(r) : new("PayPal could not read the capture.");
    private static PayPalProviderException FromVoid(SdkException<VoidPaymentError> ex) =>
        ex.Error.TryGetError(out var e) ? Typed(e) : ex.Error.TryGetNoContent(out var n) ? Raw(n) : ex.Error.TryGetRawError(out var r) ? Raw(r) : new("PayPal rejected cancellation.");
    private static PayPalProviderException FromRefund(SdkException<RefundCapturedPaymentError> ex) =>
        ex.Error.TryGetError(out var e) ? Typed(e) : ex.Error.TryGetNoContent(out var n) ? Raw(n) : ex.Error.TryGetRawError(out var r) ? Raw(r) : new("PayPal rejected refund.");
    private static PayPalProviderException FromGetRefund(SdkException<GetRefundError> ex) =>
        ex.Error.TryGetError(out var e) ? Typed(e) : ex.Error.TryGetNoContent(out var n) ? Raw(n) : ex.Error.TryGetRawError(out var r) ? Raw(r) : new("PayPal could not read the refund.");
    private static PayPalProviderException FromCreateToken(SdkException<CreatePaymentTokenError> ex) =>
        ex.Error.TryGetError1(out var e) ? Typed(e) : ex.Error.TryGetRawError(out var r) ? Raw(r) : new("PayPal rejected the card.");
    private static PayPalProviderException FromListTokens(SdkException<ListCustomerPaymentTokensError> ex) =>
        ex.Error.TryGetError1(out var e) ? Typed(e) : ex.Error.TryGetRawError(out var r) ? Raw(r) : new("PayPal could not list cards.");
    private static PayPalProviderException FromDeleteToken(SdkException<DeletePaymentTokenError> ex) =>
        ex.Error.TryGetError1(out var e) ? Typed(e) : ex.Error.TryGetRawError(out var r) ? Raw(r) : new("PayPal could not delete the card.");
}
