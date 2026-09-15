using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using PayPalServerSdk;
using PayPalServerSdk.Core.Enum;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalGateway : IPayPalGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private readonly PayPalServerSdkClient _client;
    public string Currency { get; }

    public PayPalGateway(PayPalServerSdkClient client, PayPalOptions options)
    {
        _client = client;
        Currency = options.Currency.ToUpperInvariant();
    }

    public Task<GatewayOrder> CreateOrderAsync(GatewayCreateOrderRequest request,
        CancellationToken cancellationToken) => BoundedAsync(async ct =>
    {
        try
        {
            var response = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: request.IdempotencyKey,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: new OrderRequest
                {
                    Intent = CheckoutPaymentIntent.Authorize,
                    PurchaseUnits = [new PurchaseUnitRequest
                    {
                        Amount = Amount(request.Amount, request.Currency),
                        ReferenceId = request.OrderId.ToString(CultureInfo.InvariantCulture),
                        CustomId = $"eshop-order-{request.OrderId}"
                    }]
                },
                prefer: "return=representation",
                requestOptions: null,
                ct: ct);
            return new GatewayOrder(Required(response.Id, "PayPal order id"), Wire(response.Status));
        }
        catch (SdkException<CreateOrderError> ex)
        {
            if (ex.Error.TryGetError(out var error)) throw From(error, 422, ex);
            if (ex.Error.TryGetRawError(out var raw)) throw From(raw, ex);
            throw Unknown(ex);
        }
    }, cancellationToken);

    public Task<GatewayAuthorization> AuthorizeAsync(GatewayAuthorizeRequest request,
        CancellationToken cancellationToken) => BoundedAsync(async ct =>
    {
        try
        {
            var card = request.Card is not null ? DirectCard(request.Card) : new CardRequest
            {
                VaultId = Required(request.VaultPaymentTokenId, "vault payment token id"),
                StoredCredential = new CardStoredCredential
                {
                    PaymentInitiator = PaymentInitiator.Customer,
                    PaymentType = StoredPaymentSourcePaymentType.OneTime,
                    Usage = StoredPaymentSourceUsageType.Subsequent
                }
            };
            var response = await _client.Orders.AuthorizeOrder(
                id: request.PayPalOrderId,
                payPalMockResponse: null,
                payPalRequestId: request.IdempotencyKey,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: new OrderAuthorizeRequest
                {
                    PaymentSource = new OrderAuthorizeRequestPaymentSource { Card = card }
                },
                prefer: "return=representation",
                requestOptions: null,
                ct: ct);

            var orderStatus = Wire(response.Status);
            if (orderStatus == "PAYER_ACTION_REQUIRED"
                || response.Links?.Any(x => x.Rel.Contains("payer", StringComparison.OrdinalIgnoreCase)) == true)
                throw new PaymentOperationException("browser_approval_required",
                    "PayPal requires browser approval for this card payment; this headless payment flow has stopped.", 409);

            var authorization = response.PurchaseUnits?
                .SelectMany(x => x.Payments?.Authorizations ?? [])
                .SingleOrDefault()
                ?? throw new PaymentOperationException("authorization_missing",
                    "PayPal did not return an authorization for the order.", 502);
            return Authorization(response.Id, orderStatus, authorization);
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            if (ex.Error.TryGetError(out var error)) throw From(error, 422, ex);
            if (ex.Error.TryGetRawError(out var raw)) throw From(raw, ex);
            throw Unknown(ex);
        }
    }, cancellationToken);

    public Task<GatewayAuthorization> GetAuthorizationAsync(string payPalOrderId, string authorizationId,
        CancellationToken cancellationToken) => BoundedAsync(async ct =>
    {
        try
        {
            var response = await _client.Payments.GetAuthorizedPayment(authorizationId, null, null, null, ct);
            return Authorization(payPalOrderId, string.Empty, response);
        }
        catch (SdkException<GetAuthorizedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error)) throw From(error, 404, ex);
            if (ex.Error.TryGetNoContent(out var noContent)) throw From(noContent, ex);
            if (ex.Error.TryGetRawError(out var raw)) throw From(raw, ex);
            throw Unknown(ex);
        }
    }, cancellationToken);

    public Task<GatewayAuthorization> ReauthorizeAsync(string payPalOrderId, string authorizationId,
        decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken) =>
        BoundedAsync(async ct =>
        {
            try
            {
                var response = await _client.Payments.ReauthorizePayment(
                    authorizationId: authorizationId,
                    payPalRequestId: idempotencyKey,
                    payPalAuthAssertion: null,
                    body: new ReauthorizeRequest { Amount = Money(amount, currency) },
                    prefer: "return=representation",
                    requestOptions: null,
                    ct: ct);
                return Authorization(payPalOrderId, string.Empty, response);
            }
            catch (SdkException<ReauthorizePaymentError> ex)
            {
                if (ex.Error.TryGetError(out var error))
                    throw new PaymentOperationException("authorization_not_renewable",
                        "The PayPal authorization can no longer be renewed; ask the shopper to pay again.",
                        409, error.DebugId, ex);
                if (ex.Error.TryGetNoContent(out var noContent)) throw From(noContent, ex);
                if (ex.Error.TryGetRawError(out var raw)) throw From(raw, ex);
                throw Unknown(ex);
            }
        }, cancellationToken);

    public Task<GatewayCapture> CaptureAsync(string authorizationId, decimal amount,
        string currency, string idempotencyKey, CancellationToken cancellationToken) => BoundedAsync(async ct =>
    {
        try
        {
            var response = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: new CaptureRequest { Amount = Money(amount, currency), FinalCapture = true },
                prefer: "return=representation",
                requestOptions: null,
                ct: ct);
            if (Wire(response.Status) == "PENDING" && response.Id is not null)
                response = await _client.Payments.GetCapturedPayment(response.Id, null, null, ct);
            return Capture(response);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error)) throw From(error, 422, ex);
            if (ex.Error.TryGetNoContent(out var noContent)) throw From(noContent, ex);
            if (ex.Error.TryGetRawError(out var raw)) throw From(raw, ex);
            throw Unknown(ex);
        }
        catch (SdkException<GetCapturedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error)) throw From(error, 404, ex);
            if (ex.Error.TryGetNoContent(out var noContent)) throw From(noContent, ex);
            if (ex.Error.TryGetRawError(out var raw)) throw From(raw, ex);
            throw Unknown(ex);
        }
    }, cancellationToken);

    public Task<GatewayCapture> GetCaptureAsync(string captureId, CancellationToken cancellationToken) =>
        BoundedAsync(async ct =>
        {
            try
            {
                return Capture(await _client.Payments.GetCapturedPayment(captureId, null, null, ct));
            }
            catch (SdkException<GetCapturedPaymentError> ex)
            {
                if (ex.Error.TryGetError(out var error)) throw From(error, 404, ex);
                if (ex.Error.TryGetNoContent(out var noContent)) throw From(noContent, ex);
                if (ex.Error.TryGetRawError(out var raw)) throw From(raw, ex);
                throw Unknown(ex);
            }
        }, cancellationToken);

    public Task<string> VoidAsync(string authorizationId, string idempotencyKey,
        CancellationToken cancellationToken) => BoundedAsync(async ct =>
    {
        try
        {
            var response = await _client.Payments.VoidPayment(authorizationId, null, null,
                idempotencyKey, "return=representation", null, ct);
            return Wire(response.Status);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error)) throw From(error, 409, ex);
            if (ex.Error.TryGetNoContent(out var noContent)) throw From(noContent, ex);
            if (ex.Error.TryGetRawError(out var raw)) throw From(raw, ex);
            throw Unknown(ex);
        }
    }, cancellationToken);

    public Task<GatewayRefund> RefundAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken) => BoundedAsync(async ct =>
    {
        try
        {
            var body = amount is null ? new RefundRequest() : new RefundRequest { Amount = Money(amount.Value, currency) };
            var response = await _client.Payments.RefundCapturedPayment(captureId, null,
                idempotencyKey, null, body, "return=representation", null, ct);
            return Refund(response);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error)) throw From(error, 422, ex);
            if (ex.Error.TryGetNoContent(out var noContent)) throw From(noContent, ex);
            if (ex.Error.TryGetRawError(out var raw)) throw From(raw, ex);
            throw Unknown(ex);
        }
    }, cancellationToken);

    public Task<GatewaySavedCard> SaveCardAsync(string buyerId, CardInput card, string operationId,
        CancellationToken cancellationToken) => BoundedAsync(async ct =>
    {
        try
        {
            var merchantCustomerId = StableCustomerId(buyerId);
            var setup = await _client.Vault.CreateSetupToken(
                payPalRequestId: StableOperationId(operationId, "setup-token"),
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
                            BillingAddress = Address(card.BillingAddress)
                        }
                    }
                },
                requestOptions: null,
                ct: ct);
            if (Wire(setup.Status) == "PAYER_ACTION_REQUIRED"
                || setup.Links?.Any(x => x.Rel.Contains("payer", StringComparison.OrdinalIgnoreCase)) == true)
                throw new PaymentOperationException("browser_approval_required",
                    "PayPal requires browser approval to save this card; this headless vault flow has stopped.", 409);

            var token = await _client.Vault.CreatePaymentToken(
                payPalRequestId: StableOperationId(Required(setup.Id, "setup token id"), "payment-token"),
                body: new PaymentTokenRequest
                {
                    Customer = new Customer { MerchantCustomerId = merchantCustomerId },
                    PaymentSource = new PaymentTokenRequestPaymentSource
                    {
                        Token = new VaultTokenRequest
                        {
                            Id = setup.Id!,
                            Type = VaultTokenRequestType.SetupToken
                        }
                    }
                },
                requestOptions: null,
                ct: ct);
            var safeCard = token.PaymentSource?.Card
                ?? throw new PaymentOperationException("vault_card_missing",
                    "PayPal vaulted the payment method without recognizable card details.", 502);
            return new GatewaySavedCard(
                Required(token.Id, "payment token id"),
                Required(token.Customer?.Id, "customer id"),
                Wire(safeCard.Brand),
                Required(safeCard.LastDigits, "last digits"),
                Required(safeCard.Expiry, "expiry"),
                safeCard.Name,
                WireNullable(safeCard.Type));
        }
        catch (SdkException<CreateSetupTokenError> ex)
        {
            if (ex.Error.TryGetError1(out var error)) throw From(error, 422, ex);
            if (ex.Error.TryGetRawError(out var raw)) throw From(raw, ex);
            throw Unknown(ex);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError1(out var error)) throw From(error, 422, ex);
            if (ex.Error.TryGetRawError(out var raw)) throw From(raw, ex);
            throw Unknown(ex);
        }
    }, cancellationToken);

    public Task DeleteCardAsync(string paymentTokenId, CancellationToken cancellationToken) =>
        BoundedAsync(async ct =>
        {
            try
            {
                await _client.Vault.DeletePaymentToken(paymentTokenId, null, ct);
            }
            catch (SdkException<DeletePaymentTokenError> ex)
            {
                if (ex.Error.TryGetError1(out var error)) throw From(error, 422, ex);
                if (ex.Error.TryGetRawError(out var raw)) throw From(raw, ex);
                throw Unknown(ex);
            }
        }, cancellationToken);

    public Task<IReadOnlyList<ReconciliationTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken) =>
        BoundedAsync<IReadOnlyList<ReconciliationTransaction>>(async ct =>
    {
        var output = new List<ReconciliationTransaction>();
        var windowStart = from.ToUniversalTime();
        var rangeEnd = to.ToUniversalTime();
        if (windowStart >= rangeEnd)
            throw new PaymentOperationException("invalid_date_range",
                "The reconciliation start must be before its end.", 400);

        while (windowStart < rangeEnd)
        {
            var windowEnd = windowStart.AddDays(31) < rangeEnd ? windowStart.AddDays(31) : rangeEnd;
            var page = 1;
            while (true)
            {
                try
                {
                    var response = await _client.TransactionSearch.SearchTransactions(
                        startDate: SearchDate(windowStart),
                        endDate: SearchDate(windowEnd),
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
                        ct: ct);
                    var details = response.TransactionDetails ?? [];
                    output.AddRange(details.Select(x => Transaction(x.TransactionInfo)));
                    if (response.TotalPages is int totalPages && page >= totalPages) break;
                    if (details.Count < 100) break;
                    page++;
                }
                catch (SdkException<RawError> ex)
                {
                    throw FromTransactionSearch(ex.Error, ex);
                }
            }

            windowStart = windowEnd;
        }

        return output.Distinct().ToList();
    }, cancellationToken);

    private async Task<T> BoundedAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(CallBudget);
        try { return await call(budget.Token); }
        catch (PaymentOperationException) { throw; }
        catch (JsonException ex)
        {
            throw new PaymentOperationException("provider_response_invalid",
                "PayPal returned a response that could not be processed.", 502, innerException: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentOperationException("provider_unavailable",
                "PayPal is temporarily unavailable; the operation may already have taken effect and is safe to retry.",
                503, innerException: ex);
        }
    }

    private async Task BoundedAsync(Func<CancellationToken, Task> call, CancellationToken ct)
    {
        await BoundedAsync(async innerCt => { await call(innerCt); return true; }, ct);
    }

    private static GatewayAuthorization Authorization(string? payPalOrderId, string payPalOrderStatus,
        PaymentAuthorization response) => new(
            Required(payPalOrderId, "PayPal order id"), payPalOrderStatus,
            Required(response.Id, "authorization id"), Wire(response.Status),
            WireNullable(response.StatusDetails?.Reason), ParseAmount(response.Amount),
            Required(response.Amount?.CurrencyCode, "authorization currency"), ParseDate(response.CreateTime),
            ParseDate(response.ExpirationTime));

    private static GatewayAuthorization Authorization(string? payPalOrderId, string payPalOrderStatus,
        AuthorizationWithAdditionalData response) => new(
            Required(payPalOrderId, "PayPal order id"), payPalOrderStatus,
            Required(response.Id, "authorization id"), Wire(response.Status),
            WireNullable(response.StatusDetails?.Reason), ParseAmount(response.Amount),
            Required(response.Amount?.CurrencyCode, "authorization currency"), ParseDate(response.CreateTime),
            ParseDate(response.ExpirationTime));

    private static GatewayCapture Capture(CapturedPayment response) => new(
        Required(response.Id, "capture id"), Wire(response.Status), WireNullable(response.StatusDetails?.Reason),
        ParseAmount(response.Amount), Required(response.Amount?.CurrencyCode, "capture currency"),
        ParseOptionalAmount(response.SellerReceivableBreakdown?.PaypalFee),
        ParseOptionalAmount(response.SellerReceivableBreakdown?.NetAmount), ParseDate(response.CreateTime));

    private static GatewayRefund Refund(Refund response) => new(
        Required(response.Id, "refund id"), Wire(response.Status), WireNullable(response.StatusDetails?.Reason),
        ParseAmount(response.Amount), Required(response.Amount?.CurrencyCode, "refund currency"));

    private static ReconciliationTransaction Transaction(TransactionInformation? value) => new(
        value?.TransactionId, value?.PaypalReferenceId, value?.TransactionEventCode,
        ParseDate(value?.TransactionInitiationDate), ParseDate(value?.TransactionUpdatedDate),
        ParseOptionalAmount(value?.TransactionAmount), ParseOptionalAmount(value?.FeeAmount),
        value?.TransactionAmount?.CurrencyCode, value?.TransactionStatus, value?.InvoiceId, value?.CustomField);

    private static CardRequest DirectCard(CardInput card) => new()
    {
        Name = card.Name,
        Number = card.Number,
        Expiry = card.Expiry,
        SecurityCode = card.SecurityCode,
        BillingAddress = Address(card.BillingAddress)
    };

    private static PayPalServerSdk.Models.Address Address(CardBillingAddress value) => new()
    {
        AddressLine1 = value.AddressLine1,
        AddressLine2 = value.AddressLine2,
        AdminArea2 = value.City,
        AdminArea1 = value.State,
        PostalCode = value.PostalCode,
        CountryCode = value.CountryCode.ToUpperInvariant()
    };

    private static AmountWithBreakdown Amount(decimal amount, string currency) => new()
    {
        CurrencyCode = currency,
        Value = Format(amount)
    };

    private static Money Money(decimal amount, string currency) => new()
    {
        CurrencyCode = currency,
        Value = Format(amount)
    };

    private static decimal ParseAmount(Money? money) => decimal.Parse(
        Required(money?.Value, "money value"), NumberStyles.Number, CultureInfo.InvariantCulture);
    private static decimal? ParseOptionalAmount(Money? money) => money?.Value is null ? null
        : decimal.Parse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture);
    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
    private static string SearchDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    private static string Format(decimal amount) => amount.ToString("F2", CultureInfo.InvariantCulture);
    private static string Wire<TEnum>(StringEnum<TEnum>? value) where TEnum : StringEnum<TEnum> =>
        Required(value?.Value, "provider enum value");
    private static string? WireNullable<TEnum>(StringEnum<TEnum>? value) where TEnum : StringEnum<TEnum> =>
        value?.Value;
    private static string Required(string? value, string name) => string.IsNullOrWhiteSpace(value)
        ? throw new PaymentOperationException("provider_response_invalid", $"PayPal omitted the {name}.", 502)
        : value;
    private static string StableOperationId(int orderId, string operation) => $"eshop-{orderId}-{operation}";
    private static string StableOperationId(string value, string operation) =>
        $"{value[..Math.Min(20, value.Length)]}-{operation}";
    private static string StableCustomerId(string buyerId) =>
        "eshop-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(buyerId))).ToLowerInvariant()[..24];

    private static PaymentOperationException From(PayPalServerSdk.Models.Error error, int status,
        Exception inner) => new("paypal_rejected",
        Safe(error.Message, error.Details?.Select(detail => SafeDetail(
            detail.Issue, detail.Description, detail.Field))), status, error.DebugId, inner);
    private static PaymentOperationException From(Error1 error, int status, Exception inner) =>
        new("paypal_rejected", Safe(error.Message, error.Details?.Select(detail => SafeDetail(
            detail.Issue, detail.Description, detail.Field))), status, error.DebugId, inner);
    private static PaymentOperationException From(RawError raw, Exception inner) =>
        new("paypal_rejected", "PayPal rejected the operation.", (int)raw.StatusCode, innerException: inner);
    private static PaymentOperationException FromTransactionSearch(RawError raw, Exception inner)
    {
        try
        {
            var error = raw.ReadAsJson<DefaultError>();
            if (error is not null)
                return new PaymentOperationException("paypal_rejected",
                    Safe(error.Message, error.Details?.Select(detail => SafeDetail(
                        detail.Issue, detail.Description, detail.Field))),
                    (int)raw.StatusCode, error.DebugId, inner);
        }
        catch (JsonException)
        {
            // The raw response is intentionally not exposed when it does not match the documented safe shape.
        }

        return From(raw, inner);
    }
    private static PaymentOperationException Unknown(Exception inner) =>
        new("paypal_error", "PayPal rejected the operation.", 502, innerException: inner);
    private static string Safe(string? message, IEnumerable<string>? details)
    {
        var summary = string.IsNullOrWhiteSpace(message) ? "PayPal rejected the operation." : Clip(message);
        var safeDetails = details?.Where(detail => !string.IsNullOrWhiteSpace(detail)).Take(5).ToArray();
        return safeDetails is { Length: > 0 }
            ? $"{summary} Details: {string.Join("; ", safeDetails)}"
            : summary;
    }

    private static string SafeDetail(string issue, string? description, string? field)
    {
        var parts = new List<string> { $"issue={Clip(issue)}" };
        if (!string.IsNullOrWhiteSpace(field)) parts.Add($"field={Clip(field)}");
        if (!string.IsNullOrWhiteSpace(description)) parts.Add($"description={Clip(description)}");
        return string.Join(", ", parts);
    }

    private static string Clip(string value)
    {
        var sanitized = new string(value.Where(character => !char.IsControl(character)).ToArray()).Trim();
        return sanitized[..Math.Min(sanitized.Length, 300)];
    }
}
