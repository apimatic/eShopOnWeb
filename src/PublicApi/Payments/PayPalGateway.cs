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
using Microsoft.Extensions.Options;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PayPalGateway : IPayPalGateway
{
    private readonly PayPalServerSdk.PayPalServerSdkClient _client;
    private readonly PayPalOptions _options;
    private readonly PayPalCallContext _callContext;

    public PayPalGateway(PayPalServerSdk.PayPalServerSdkClient client,
        IOptions<PayPalOptions> options, PayPalCallContext callContext)
    {
        _client = client;
        _options = options.Value;
        _callContext = callContext;
    }

    public async Task<ProviderAuthorization> AuthorizeAsync(string externalReference, int orderId,
        decimal amount, string currency,
        ProviderCard? card, string? vaultId, CancellationToken cancellationToken)
    {
        var value = MoneyValue(amount);
        var providerOrder = await Bounded(ct => _client.Orders.CreateOrder(
            payPalMockResponse: null,
            payPalRequestId: RequestId("create", externalReference),
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
                        ReferenceId = externalReference,
                        InvoiceId = externalReference,
                        CustomId = orderId.ToString(CultureInfo.InvariantCulture),
                        Amount = new AmountWithBreakdown { CurrencyCode = currency, Value = value }
                    }
                }
            },
            prefer: "return=representation",
            requestOptions: null,
            ct: ct), cancellationToken);

        if (string.IsNullOrWhiteSpace(providerOrder.Id))
        {
            throw UnknownResponse();
        }

        var sdkCard = vaultId is not null
            ? new PayPalServerSdk.Models.CardRequest { VaultId = vaultId }
            : ToSdkCard(card ?? throw new InvalidOperationException("Card details are required."));

        var authorized = await Bounded(ct => _client.Orders.AuthorizeOrder(
            id: providerOrder.Id,
            payPalMockResponse: null,
            payPalRequestId: RequestId("authorize", externalReference),
            payPalClientMetadataId: null,
            payPalAuthAssertion: null,
            body: new OrderAuthorizeRequest
            {
                PaymentSource = new OrderAuthorizeRequestPaymentSource { Card = sdkCard }
            },
            prefer: "return=representation",
            requestOptions: null,
            ct: ct), cancellationToken);

        var orderStatus = authorized.Status?.Value;
        if (orderStatus == OrderStatus.PayerActionRequired.Value)
        {
            throw new PaymentApiException(409, "paypal_payer_action_required",
                "PayPal requires browser approval for this card. This headless payment flow cannot continue.");
        }

        var authorization = authorized.PurchaseUnits?
            .SelectMany(p => p.Payments?.Authorizations ?? Array.Empty<AuthorizationWithAdditionalData>())
            .FirstOrDefault();
        if (authorization?.Id is null || authorization.Amount is null)
        {
            throw UnknownResponse();
        }

        EnsureAmount(authorization.Amount, amount, currency);
        return new ProviderAuthorization(
            authorized.Id ?? providerOrder.Id,
            orderStatus ?? providerOrder.Status?.Value ?? "UNKNOWN",
            authorization.Id,
            authorization.Status?.Value ?? "UNKNOWN",
            ParseMoney(authorization.Amount),
            ParseDate(authorization.CreateTime),
            ParseDate(authorization.ExpirationTime),
            ParseDate(authorization.UpdateTime),
            authorization.ProcessorResponse?.ResponseCode?.Value,
            authorization.ProcessorResponse?.AvsCode?.Value,
            authorization.ProcessorResponse?.CvvCode?.Value);
    }

    public async Task<ProviderCapture> CaptureAsync(string externalReference, int orderId,
        string authorizationId, decimal amount, string currency,
        DateTimeOffset? authorizationCreatedAt, CancellationToken cancellationToken)
    {
        var current = await Bounded(ct => _client.Payments.GetAuthorizedPayment(
            authorizationId: authorizationId, payPalMockResponse: null, payPalAuthAssertion: null,
            requestOptions: null, ct: ct), cancellationToken);
        EnsureAmount(current.Amount, amount, currency);

        var currentId = current.Id ?? authorizationId;
        var currentStatus = current.Status?.Value ?? "UNKNOWN";
        if (authorizationCreatedAt is not null && authorizationCreatedAt.Value.AddDays(3) <= DateTimeOffset.UtcNow)
        {
            try
            {
                var renewed = await Bounded(ct => _client.Payments.ReauthorizePayment(
                    authorizationId: currentId,
                    payPalRequestId: RequestId("reauthorize", externalReference),
                    payPalAuthAssertion: null,
                    body: new ReauthorizeRequest
                    {
                        Amount = new Money { CurrencyCode = currency, Value = MoneyValue(amount) }
                    },
                    prefer: "return=representation", requestOptions: null, ct: ct), cancellationToken);
                currentId = renewed.Id ?? currentId;
                currentStatus = renewed.Status?.Value ?? currentStatus;
                EnsureAmount(renewed.Amount, amount, currency);
            }
            catch (PaymentApiException ex) when (ex.StatusCode is >= 400 and < 500)
            {
                throw new PaymentApiException(409, "authorization_cannot_be_renewed",
                    "The PayPal authorization can no longer be renewed. Ask the shopper to submit a new payment before fulfilment.",
                    ex.ProviderDebugId, ex);
            }
        }

        var captured = await Bounded(ct => _client.Payments.CaptureAuthorizedPayment(
            authorizationId: currentId,
            payPalMockResponse: null,
            payPalRequestId: RequestId("capture", externalReference),
            payPalAuthAssertion: null,
            body: new CaptureRequest
            {
                Amount = new Money { CurrencyCode = currency, Value = MoneyValue(amount) },
                FinalCapture = true,
                InvoiceId = externalReference
            },
            prefer: "return=representation", requestOptions: null, ct: ct), cancellationToken);
        if (captured.Id is null || captured.Amount is null)
        {
            throw UnknownResponse();
        }

        EnsureAmount(captured.Amount, amount, currency);
        return new ProviderCapture(
            currentId,
            currentStatus,
            captured.Id,
            captured.Status?.Value ?? "UNKNOWN",
            ParseMoney(captured.Amount),
            captured.SellerReceivableBreakdown?.PaypalFee is null ? null : ParseMoney(captured.SellerReceivableBreakdown.PaypalFee),
            captured.SellerReceivableBreakdown?.NetAmount is null ? null : ParseMoney(captured.SellerReceivableBreakdown.NetAmount),
            ParseDate(captured.CreateTime),
            captured.ProcessorResponse?.ResponseCode?.Value,
            captured.ProcessorResponse?.AvsCode?.Value,
            captured.ProcessorResponse?.CvvCode?.Value);
    }

    public async Task<ProviderVoid> VoidAsync(string externalReference, string authorizationId,
        CancellationToken cancellationToken)
    {
        var result = await Bounded(ct => _client.Payments.VoidPayment(
            authorizationId: authorizationId, payPalMockResponse: null, payPalAuthAssertion: null,
            payPalRequestId: RequestId("void", externalReference),
            prefer: "return=representation", requestOptions: null, ct: ct), cancellationToken);
        return new ProviderVoid(result.Id ?? authorizationId, result.Status?.Value ?? "UNKNOWN",
            ParseDate(result.UpdateTime));
    }

    public async Task<ProviderRefund> RefundAsync(string externalReference, int orderId,
        string captureId, decimal? amount, string currency, string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var body = new PayPalServerSdk.Models.RefundRequest
        {
            CustomId = orderId.ToString(CultureInfo.InvariantCulture),
            InvoiceId = externalReference
        };
        if (amount is not null)
        {
            body = body with
            {
                Amount = new Money { CurrencyCode = currency, Value = MoneyValue(amount.Value) }
            };
        }

        var result = await Bounded(ct => _client.Payments.RefundCapturedPayment(
            captureId: captureId, payPalMockResponse: null,
            payPalRequestId: RequestId("refund", $"{externalReference}:{idempotencyKey}"),
            payPalAuthAssertion: null, body: body, prefer: "return=representation",
            requestOptions: null, ct: ct), cancellationToken);
        if (result.Id is null || result.Amount is null)
        {
            throw UnknownResponse();
        }

        return new ProviderRefund(result.Id, result.Status?.Value ?? "UNKNOWN",
            ParseMoney(result.Amount), ParseDate(result.UpdateTime ?? result.CreateTime));
    }

    public async Task<ProviderPaymentMethod> SavePaymentMethodAsync(string buyerId, ProviderCard card,
        CancellationToken cancellationToken)
    {
        var result = await Bounded(ct => _client.Vault.CreatePaymentToken(
            payPalRequestId: RequestId("vault", buyerId),
            body: new PaymentTokenRequest
            {
                Customer = new Customer { MerchantCustomerId = StableCustomerId(buyerId) },
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
            }, requestOptions: null, ct: ct), cancellationToken);
        var safeCard = result.PaymentSource?.Card;
        if (result.Id is null || safeCard is null)
        {
            throw UnknownResponse();
        }

        return new ProviderPaymentMethod(result.Id, result.Customer?.Id,
            safeCard.Brand?.Value, safeCard.LastDigits, safeCard.Expiry, safeCard.Type?.Value);
    }

    public Task DeletePaymentMethodAsync(string vaultId, CancellationToken cancellationToken) =>
        Bounded(async ct =>
        {
            await _client.Vault.DeletePaymentToken(id: vaultId, requestOptions: null, ct: ct);
            return true;
        }, cancellationToken);

    public async Task<ProviderTransactionReport> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var transactions = new List<ProviderTransaction>();
        DateTimeOffset? refreshedAt = null;
        var page = 1;
        while (true)
        {
            var response = await Bounded(ct => _client.TransactionSearch.SearchTransactions(
                startDate: PayPalDate(from),
                endDate: PayPalDate(to),
                transactionId: null, transactionType: null, transactionStatus: null,
                transactionAmount: null, transactionCurrency: null, paymentInstrumentType: null,
                storeId: null, terminalId: null, fields: "transaction_info",
                balanceAffectingRecordsOnly: "Y", pageSize: 100, page: page,
                requestOptions: null, ct: ct), cancellationToken, includeSafeRawDiagnostics: true);

            refreshedAt ??= ParseDate(response.LastRefreshedDatetime);
            var details = response.TransactionDetails ?? Array.Empty<TransactionDetails>();
            foreach (var detail in details)
            {
                var info = detail.TransactionInfo;
                if (info?.TransactionId is null)
                {
                    continue;
                }

                transactions.Add(new ProviderTransaction(info.TransactionId, info.PaypalReferenceId,
                    info.InvoiceId, info.CustomField, info.TransactionEventCode, info.TransactionStatus,
                    info.TransactionAmount is null ? null : ParseMoney(info.TransactionAmount),
                    info.FeeAmount is null ? null : ParseMoney(info.FeeAmount),
                    info.TransactionAmount?.CurrencyCode,
                    ParseDate(info.TransactionInitiationDate)));
            }

            if (details.Count == 0 || (response.TotalPages is not null && page >= response.TotalPages.Value))
            {
                break;
            }
            page++;
        }

        return new ProviderTransactionReport(transactions, refreshedAt);
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call,
        CancellationToken cancellationToken, bool includeSafeRawDiagnostics = false)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.TotalCallTimeout);
        _callContext.LastStatus = null;
        try
        {
            return await call(timeout.Token);
        }
        catch (PaymentApiException) { throw; }
        catch (SdkException<RawError> ex) when (includeSafeRawDiagnostics)
        {
            throw FromTransactionSearch(ex.Error, ex);
        }
        catch (Exception ex) when (TranslateSdkException(ex) is { } translated)
        {
            throw translated;
        }
        catch (JsonException ex)
        {
            var status = _callContext.LastStatus;
            if (status is not null && (int)status.Value >= 400)
            {
                throw new PaymentApiException((int)status.Value, "paypal_rejected_request",
                    "PayPal rejected the request but returned an unreadable error response.", null, ex);
            }
            throw new PaymentApiException(502, "paypal_invalid_response",
                "PayPal returned a response that could not be processed.", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentApiException(503, "paypal_unavailable",
                "PayPal is temporarily unavailable. Retry with the same request.", null, ex);
        }
    }

    private static PaymentApiException? TranslateSdkException(Exception ex) => ex switch
    {
        SdkException<CreateOrderError> e => From(e.Error.TryGetError(out var x) ? x : null, e),
        SdkException<AuthorizeOrderError> e => From(e.Error.TryGetError(out var x) ? x : null, e),
        SdkException<GetOrderError> e => From(e.Error.TryGetError(out var x) ? x : null, e),
        SdkException<GetAuthorizedPaymentError> e => From(e.Error.TryGetError(out var x) ? x : null, e),
        SdkException<ReauthorizePaymentError> e => From(e.Error.TryGetError(out var x) ? x : null, e),
        SdkException<CaptureAuthorizedPaymentError> e => From(e.Error.TryGetError(out var x) ? x : null, e),
        SdkException<GetCapturedPaymentError> e => From(e.Error.TryGetError(out var x) ? x : null, e),
        SdkException<VoidPaymentError> e => From(e.Error.TryGetError(out var x) ? x : null, e),
        SdkException<RefundCapturedPaymentError> e => From(e.Error.TryGetError(out var x) ? x : null, e),
        SdkException<GetRefundError> e => From(e.Error.TryGetError(out var x) ? x : null, e),
        SdkException<CreatePaymentTokenError> e => From(e.Error.TryGetError1(out var x) ? x : null, e),
        SdkException<GetPaymentTokenError> e => From(e.Error.TryGetError1(out var x) ? x : null, e),
        SdkException<DeletePaymentTokenError> e => From(e.Error.TryGetError1(out var x) ? x : null, e),
        SdkException<RawError> e => From(e.Error, e),
        _ => null
    };

    private static PaymentApiException From(Error? error, Exception inner) => new(
        422,
        error?.Name ?? "paypal_rejected_request",
        SafeProviderMessage(error?.Message, error?.Details?.FirstOrDefault()?.Description),
        error?.DebugId,
        inner);

    private static PaymentApiException From(Error1? error, Exception inner) => new(
        422,
        error?.Name ?? "paypal_rejected_request",
        SafeProviderMessage(error?.Message, error?.Details?.FirstOrDefault()?.Description),
        error?.DebugId,
        inner);

    private static PaymentApiException From(RawError error, Exception inner) => new(
        (int)error.StatusCode, "paypal_rejected_request", "PayPal rejected the request.", null, inner);

    private static PaymentApiException FromTransactionSearch(RawError error, Exception inner)
    {
        const int maxPayloadBytes = 16 * 1024;
        var payload = error.ReadAsBytes();
        if (payload.Length == 0 || payload.Length > maxPayloadBytes)
        {
            return From(error, inner);
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            var name = SafeJsonString(root, "name", 128) ?? "paypal_rejected_request";
            var message = SafeJsonString(root, "message", 1024);
            var debugId = SafeJsonString(root, "debug_id", 128);
            var details = new List<string>();
            if (root.TryGetProperty("details", out var detailArray)
                && detailArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in detailArray.EnumerateArray().Take(8))
                {
                    if (detail.ValueKind != JsonValueKind.Object) continue;
                    var issue = SafeJsonString(detail, "issue", 256);
                    var field = SafeJsonString(detail, "field", 256);
                    var description = SafeJsonString(detail, "description", 512);
                    var safeDetail = string.Join(" ", new[]
                    {
                        issue is null ? null : $"Issue: {issue}.",
                        field is null ? null : $"Field: {field}.",
                        description
                    }.Where(value => !string.IsNullOrWhiteSpace(value)));
                    if (safeDetail.Length > 0) details.Add(safeDetail);
                }
            }

            return new PaymentApiException((int)error.StatusCode, name,
                SafeProviderMessage(message, details.Count == 0 ? null : string.Join(" ", details)),
                debugId, inner);
        }
        catch (JsonException)
        {
            return From(error, inner);
        }
    }

    private static string? SafeJsonString(JsonElement parent, string propertyName, int maxLength)
    {
        if (!parent.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = property.GetString();
        if (string.IsNullOrWhiteSpace(value)) return null;
        return new string(value.Select(character => char.IsControl(character) ? ' ' : character)
            .Take(maxLength).ToArray()).Trim();
    }

    private static string SafeProviderMessage(string? message, string? detail) =>
        string.Join(" ", new[] { message, detail }.Where(s => !string.IsNullOrWhiteSpace(s))) is { Length: > 0 } text
            ? text
            : "PayPal rejected the request.";

    private static PayPalServerSdk.Models.CardRequest ToSdkCard(ProviderCard card) => new()
    {
        Name = card.Name,
        Number = card.Number,
        Expiry = card.Expiry,
        SecurityCode = card.SecurityCode,
        BillingAddress = ToAddress(card.BillingAddress)
    };

    private static Address ToAddress(BillingAddressRequest address) => new()
    {
        AddressLine1 = address.AddressLine1,
        AddressLine2 = address.AddressLine2,
        AdminArea2 = address.City,
        AdminArea1 = address.State,
        PostalCode = address.PostalCode,
        CountryCode = address.CountryCode
    };

    private static decimal ParseMoney(Money money) =>
        decimal.Parse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture);

    private static void EnsureAmount(Money? money, decimal expectedAmount, string expectedCurrency)
    {
        if (money is null || !string.Equals(money.CurrencyCode, expectedCurrency, StringComparison.OrdinalIgnoreCase)
            || ParseMoney(money) != expectedAmount)
        {
            throw new PaymentApiException(502, "paypal_amount_mismatch",
                "PayPal reported an amount or currency that does not match the order. Reconcile the payment before continuing.");
        }
    }

    private static string MoneyValue(decimal amount)
    {
        if (amount <= 0 || decimal.Round(amount, 2, MidpointRounding.ToEven) != amount)
        {
            throw new PaymentApiException(422, "invalid_amount", "The payment amount must be positive and precise to one cent.");
        }
        return amount.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;

    private static string PayPalDate(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string StableCustomerId(string buyerId) => "eshop-" + Hash(buyerId)[..24];
    private static string RequestId(string operation, string key) => $"eshop-{operation}-{Hash(key)[..24]}";
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static PaymentApiException UnknownResponse() => new(502, "paypal_invalid_response",
        "PayPal returned an incomplete response. Reconcile provider state before retrying with a new request.");
}
