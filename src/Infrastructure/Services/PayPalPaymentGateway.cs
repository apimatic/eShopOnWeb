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
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.Extensions.Logging;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using PaypalOrderStatus = PayPalServerSdk.Models.Enums.OrderStatus;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// PayPal implementation of the payment gateway. Every SDK call goes through Bounded (one
/// total call budget) and is translated at this boundary: typed SdkException errors become
/// PaymentGatewayException with a caller-safe message, transport failures become 502s, and a
/// 2xx body the SDK cannot deserialize becomes "response could not be processed" — never a
/// phantom rejection. Full card numbers pass through to PayPal only; they are never logged
/// or included in any return value.
/// </summary>
public class PayPalPaymentGateway : IPaymentGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Prefix for gateway-generated PayPal-Request-Ids. PayPal keys idempotency per merchant,
    /// so a key derived only from an order id collides across runs whenever local order ids
    /// restart (the in-memory test database). Within a run the keys stay deterministic per
    /// logical operation, so retries and double-clicks still dedupe. Caller-supplied refund
    /// keys are NOT prefixed — they are the caller's own contract.
    /// </summary>
    private readonly string _runId = Guid.NewGuid().ToString("N");

    private readonly PayPalServerSdkClient _client;
    private readonly ILogger<PayPalPaymentGateway> _logger;

    public PayPalPaymentGateway(PayPalServerSdkClient client, ILogger<PayPalPaymentGateway> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<GatewayAuthorizationResult> AuthorizeCardPaymentAsync(
        int orderId, decimal amount, string currency, CardPaymentDetails card, string idempotencyKey, CancellationToken ct)
    {
        var cardRequest = new CardRequest
        {
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            Name = card.Name,
            BillingAddress = new Address { CountryCode = card.BillingCountryCode }
        };
        return await AuthorizeAsync(orderId, amount, currency, new PaymentSource { Card = cardRequest }, idempotencyKey, ct);
    }

    public async Task<GatewayAuthorizationResult> AuthorizeSavedCardPaymentAsync(
        int orderId, decimal amount, string currency, string vaultPaymentTokenId, string idempotencyKey, CancellationToken ct)
    {
        return await AuthorizeAsync(orderId, amount, currency,
            new PaymentSource { Card = new CardRequest { VaultId = vaultPaymentTokenId } }, idempotencyKey, ct);
    }

    private async Task<GatewayAuthorizationResult> AuthorizeAsync(
        int orderId, decimal amount, string currency, PaymentSource paymentSource, string idempotencyKey, CancellationToken ct)
    {
        var orderRequest = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    ReferenceId = $"order-{orderId}",
                    // CustomId carries the stable order reference used by reconciliation.
                    CustomId = $"order-{orderId}",
                    // PayPal enforces per-merchant invoice-id uniqueness, so a retry of a pay
                    // attempt must NOT reuse one: unique per attempt, order-prefixed.
                    InvoiceId = $"order-{orderId}-{Guid.NewGuid():N}",
                    Description = $"eShopOnWeb order #{orderId}",
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = currency,
                        Value = FormatMoney(amount)
                    }
                }
            },
            PaymentSource = paymentSource
        };

        Order order;
        try
        {
            order = await Bounded(token => _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: $"eshop-{_runId}-create-order-{orderId}",
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: orderRequest,
                prefer: "return=representation",
                requestOptions: null,
                ct: token), ct);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw Translate("create order", ex.Error);
        }

        if (order.Id is null)
        {
            throw Unprocessable("create order");
        }
        if (order.Status == PaypalOrderStatus.PayerActionRequired)
        {
            throw new BuyerActionRequiredException(
                "PayPal requires the shopper to approve this card payment in a browser (3D Secure challenge). " +
                "This integration is server-to-server only and cannot complete the challenge.");
        }

        // With a card payment_source, PayPal may authorize inline at create time; use that
        // authorization when present and only call AuthorizeOrder when it is absent.
        var authorization = order.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();

        if (authorization?.Id is null)
        {
            try
            {
                var authorizeResponse = await Bounded(token => _client.Orders.AuthorizeOrder(
                    id: order.Id,
                    payPalMockResponse: null,
                    payPalRequestId: $"{_runId}-{idempotencyKey}",
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: null,
                    prefer: "return=representation",
                    requestOptions: null,
                    ct: token), ct);
                authorization = authorizeResponse.PurchaseUnits?
                    .FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
            }
            catch (SdkException<AuthorizeOrderError> ex) when (HasIssue(ex.Error, "ORDER_ALREADY_AUTHORIZED"))
            {
                // Another attempt authorized first (e.g. a retried double-click): re-read it.
                authorization = (await GetOrderAsync(order.Id, ct)).PurchaseUnits?
                    .FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
            }
            catch (SdkException<AuthorizeOrderError> ex)
            {
                throw Translate("authorize order", ex.Error);
            }
        }

        if (authorization?.Id is null)
        {
            throw Unprocessable("authorize order");
        }
        if (authorization.Status == AuthorizationStatus.Denied)
        {
            throw new PaymentGatewayException(
                $"PayPal declined the card authorization for order {orderId}.",
                providerStatusCode: 402, providerErrorName: "CARD_DECLINED");
        }

        return new GatewayAuthorizationResult(
            order.Id,
            authorization.Id,
            authorization.Status?.Value,
            ParseDate(authorization.ExpirationTime));
    }

    private async Task<Order> GetOrderAsync(string payPalOrderId, CancellationToken ct)
    {
        try
        {
            return await Bounded(token => _client.Orders.GetOrder(
                id: payPalOrderId,
                fields: null,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                requestOptions: null,
                ct: token), ct);
        }
        catch (SdkException<GetOrderError> ex)
        {
            throw Translate("get order", ex.Error);
        }
    }

    private static bool HasIssue(ApiError error, string issue)
    {
        if (TryGetErrorPayload(error, out var payload))
        {
            return payload!.Details?.Any(d => d.Issue == issue) == true;
        }
        return false;
    }

    private static bool TryGetErrorPayload(ApiError error, out Error? payload)
    {
        switch (error)
        {
            case AuthorizeOrderError e when e.TryGetError(out var err): payload = err; return true;
            case CreateOrderError e when e.TryGetError(out var err): payload = err; return true;
            case GetOrderError e when e.TryGetError(out var err): payload = err; return true;
            default: payload = null; return false;
        }
    }

    public async Task<GatewayAuthorizationStatus> GetAuthorizationAsync(string authorizationId, CancellationToken ct)
    {
        try
        {
            var authorization = await Bounded(token => _client.Payments.GetAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                requestOptions: null,
                ct: token), ct);

            return new GatewayAuthorizationStatus(
                authorization.Id ?? authorizationId,
                authorization.Status?.Value,
                ParseDate(authorization.ExpirationTime),
                ParseMoney(authorization.Amount),
                authorization.Amount?.CurrencyCode);
        }
        catch (SdkException<GetAuthorizedPaymentError> ex)
        {
            throw Translate("get authorization", ex.Error);
        }
    }

    public async Task<GatewayAuthorizationStatus> ReauthorizeAsync(
        string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken ct)
    {
        try
        {
            var renewed = await Bounded(token => _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: $"{_runId}-{idempotencyKey}",
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest
                {
                    Amount = new Money { CurrencyCode = currency, Value = FormatMoney(amount) }
                },
                prefer: "return=representation",
                requestOptions: null,
                ct: token), ct);

            if (renewed.Id is null)
            {
                throw Unprocessable("reauthorize payment");
            }
            return new GatewayAuthorizationStatus(
                renewed.Id,
                renewed.Status?.Value,
                ParseDate(renewed.ExpirationTime),
                ParseMoney(renewed.Amount),
                renewed.Amount?.CurrencyCode);
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            throw Translate("reauthorize payment", ex.Error);
        }
    }

    public async Task<GatewayCaptureResult> CaptureAsync(string authorizationId, int orderId, string idempotencyKey, CancellationToken ct)
    {
        try
        {
            var capture = await Bounded(token => _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: $"{_runId}-{idempotencyKey}",
                payPalAuthAssertion: null,
                body: new CaptureRequest
                {
                    FinalCapture = true,
                    InvoiceId = $"order-{orderId}-capture-{Guid.NewGuid():N}",
                    NoteToPayer = $"eShopOnWeb order #{orderId}"
                },
                prefer: "return=representation",
                requestOptions: null,
                ct: token), ct);

            if (capture.Id is null)
            {
                throw Unprocessable("capture payment");
            }

            // Gross/fee/net come from PayPal's seller receivable breakdown (requested via
            // prefer: return=representation); without it the capture cannot be accounted for.
            var breakdown = capture.SellerReceivableBreakdown;
            var gross = ParseMoney(breakdown?.GrossAmount) ?? throw Unprocessable("capture payment");
            return new GatewayCaptureResult(
                capture.Id,
                capture.Status?.Value,
                gross,
                ParseMoney(breakdown?.PaypalFee),
                ParseMoney(breakdown?.NetAmount),
                breakdown?.GrossAmount?.CurrencyCode);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            throw Translate("capture payment", ex.Error);
        }
    }

    public async Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct)
    {
        try
        {
            // NOTE: on this operation payPalRequestId is the 4th parameter, not the 3rd.
            await Bounded(token => _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: $"{_runId}-{idempotencyKey}",
                prefer: "return=representation",
                requestOptions: null,
                ct: token), ct);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            throw Translate("void authorization", ex.Error);
        }
    }

    public async Task<GatewayRefundResult> RefundAsync(
        string captureId, int orderId, decimal amount, string currency, string idempotencyKey, string? note, CancellationToken ct)
    {
        try
        {
            var refund = await Bounded(token => _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: new RefundRequest
                {
                    Amount = new Money { CurrencyCode = currency, Value = FormatMoney(amount) },
                    CustomId = $"order-{orderId}",
                    InvoiceId = $"order-{orderId}-refund-{idempotencyKey}",
                    NoteToPayer = note
                },
                prefer: "return=representation",
                requestOptions: null,
                ct: token), ct);

            if (refund.Id is null)
            {
                throw Unprocessable("refund capture");
            }
            return new GatewayRefundResult(
                refund.Id,
                refund.Status?.Value,
                ParseMoney(refund.Amount) ?? amount,
                refund.Amount?.CurrencyCode ?? currency);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            throw Translate("refund capture", ex.Error);
        }
    }

    public async Task<GatewayVaultedCard> VaultCardAsync(
        CardPaymentDetails card, string merchantCustomerId, string idempotencyKey, CancellationToken ct)
    {
        try
        {
            var response = await Bounded(token => _client.Vault.CreatePaymentToken(
                payPalRequestId: $"{_runId}-{idempotencyKey}",
                body: new PaymentTokenRequest
                {
                    PaymentSource = new PaymentTokenRequestPaymentSource
                    {
                        Card = new PaymentTokenRequestCard
                        {
                            Number = card.Number,
                            Expiry = card.Expiry,
                            SecurityCode = card.SecurityCode,
                            Name = card.Name,
                            BillingAddress = new Address { CountryCode = card.BillingCountryCode }
                        }
                    },
                    Customer = new Customer { MerchantCustomerId = merchantCustomerId }
                },
                requestOptions: null,
                ct: token), ct);

            if (response.Id is null)
            {
                throw Unprocessable("vault card");
            }
            return new GatewayVaultedCard(
                response.Id,
                response.Customer?.Id,
                response.PaymentSource?.Card?.Brand?.Value,
                response.PaymentSource?.Card?.LastDigits,
                response.PaymentSource?.Card?.Expiry);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            throw TranslateVault("vault card", ex.Error);
        }
    }

    public async Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken ct)
    {
        try
        {
            await Bounded(token => _client.Vault.DeletePaymentToken(
                id: paymentTokenId,
                requestOptions: null,
                ct: token), ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            throw TranslateVault("delete vaulted card", ex.Error);
        }
    }

    public async Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var all = new List<GatewayTransaction>();
        var page = 1;
        var totalPages = 1;

        do
        {
            SearchResponse response;
            try
            {
                response = await Bounded(token => _client.TransactionSearch.SearchTransactions(
                    startDate: FormatSearchDate(from),
                    endDate: FormatSearchDate(to),
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
                    ct: token), ct);
            }
            catch (SdkException<RawError> ex)
            {
                // Transaction search is the SDK's only Case-B operation: the error body has no
                // typed accessor, so read status and body straight off the RawError.
                throw FromRaw("search transactions", ex.Error);
            }

            if (response.TransactionDetails is not null)
            {
                foreach (var detail in response.TransactionDetails)
                {
                    var info = detail.TransactionInfo;
                    if (info is null)
                    {
                        continue;
                    }
                    all.Add(new GatewayTransaction(
                        info.TransactionId,
                        info.TransactionStatus,
                        ParseMoney(info.TransactionAmount),
                        info.TransactionAmount?.CurrencyCode,
                        ParseMoney(info.FeeAmount),
                        ParseDate(info.TransactionInitiationDate),
                        ParseDate(info.TransactionUpdatedDate),
                        info.InvoiceId,
                        info.CustomField,
                        info.PaypalReferenceId,
                        info.PaypalReferenceIdType?.Value));
                }
            }

            totalPages = response.TotalPages ?? page;
            page++;
        }
        while (page <= totalPages);

        return all;
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        try
        {
            return await call(cts.Token);
        }
        catch (JsonException ex)
        {
            // A 2xx whose body drifted from the SDK model: outcome unknown — never a rejection.
            throw new PaymentGatewayException(
                "The payment provider returned a response that could not be processed.", null, null, null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentGatewayException(
                "The payment provider could not be reached; the operation's outcome is unknown and safe to retry under the same idempotency key.",
                503, "PROVIDER_UNREACHABLE", null, ex);
        }
    }

    private async Task Bounded(Func<CancellationToken, Task> call, CancellationToken ct)
    {
        await Bounded(async token =>
        {
            await call(token);
            return true;
        }, ct);
    }

    private PaymentGatewayException Translate(string operation, ApiError error)
    {
        // One branch per accessor the operation's error type declares; TryGetRawError last.
        if (TryReadTypedError(error, out var typed))
        {
            return typed!;
        }
        if (error.TryGetRawError(out var raw))
        {
            return FromRaw(operation, raw);
        }
        return new PaymentGatewayException($"PayPal could not {operation}: the request was rejected.");
    }

    private static bool TryReadTypedError(ApiError error, out PaymentGatewayException? result)
    {
        // The typed TryGetError/TryGetError1 accessors live on the concrete {Operation}Error
        // types, not on ApiError — dispatch to the concrete type here, where it is known.
        switch (error)
        {
            case CreateOrderError e when e.TryGetError(out var err):
                result = FromTyped("create order", err.Name, err.Message, err.DebugId, err.Details?.Select(d => d.Issue));
                return true;
            case GetOrderError e when e.TryGetError(out var err):
                result = FromTyped("get order", err.Name, err.Message, err.DebugId, err.Details?.Select(d => d.Issue));
                return true;
            case AuthorizeOrderError e when e.TryGetError(out var err):
                result = FromTyped("authorize order", err.Name, err.Message, err.DebugId, err.Details?.Select(d => d.Issue));
                return true;
            case GetAuthorizedPaymentError e when e.TryGetError(out var err):
                result = FromTyped("get authorization", err.Name, err.Message, err.DebugId, err.Details?.Select(d => d.Issue));
                return true;
            case ReauthorizePaymentError e when e.TryGetError(out var err):
                result = FromTyped("reauthorize payment", err.Name, err.Message, err.DebugId, err.Details?.Select(d => d.Issue));
                return true;
            case CaptureAuthorizedPaymentError e when e.TryGetError(out var err):
                result = FromTyped("capture payment", err.Name, err.Message, err.DebugId, err.Details?.Select(d => d.Issue));
                return true;
            case VoidPaymentError e when e.TryGetError(out var err):
                result = FromTyped("void authorization", err.Name, err.Message, err.DebugId, err.Details?.Select(d => d.Issue));
                return true;
            case RefundCapturedPaymentError e when e.TryGetError(out var err):
                result = FromTyped("refund capture", err.Name, err.Message, err.DebugId, err.Details?.Select(d => d.Issue));
                return true;
            default:
                result = null;
                return false;
        }
    }

    private PaymentGatewayException TranslateVault(string operation, ApiError error)
    {
        switch (error)
        {
            case CreatePaymentTokenError e when e.TryGetError1(out var err):
                return FromTyped(operation, err.Name, err.Message, err.DebugId, err.Details?.Select(d => d.Issue));
            case DeletePaymentTokenError e when e.TryGetError1(out var err):
                return FromTyped(operation, err.Name, err.Message, err.DebugId, err.Details?.Select(d => d.Issue));
        }
        if (error.TryGetRawError(out var raw))
        {
            return FromRaw(operation, raw);
        }
        return new PaymentGatewayException($"PayPal could not {operation}: the request was rejected.");
    }

    private static PaymentGatewayException FromTyped(
        string operation, string? name, string? message, string? debugId, IEnumerable<string>? issues)
    {
        var detail = message ?? "the request was rejected";
        if (issues is not null)
        {
            var issueList = issues.Where(i => !string.IsNullOrWhiteSpace(i)).ToList();
            if (issueList.Count > 0)
            {
                detail += $" ({string.Join(", ", issueList)})";
            }
        }
        // Typed error bodies arrive only for provider rejections (4xx per the operation
        // contract); the exact status is not carried on the typed model, so surface 422.
        return new PaymentGatewayException($"PayPal could not {operation}: {detail}", 422, name, debugId);
    }

    private static PaymentGatewayException FromRaw(string operation, RawError raw)
    {
        // The raw body is deliberately not surfaced: it is not caller-safe.
        var status = (int)raw.StatusCode;
        return new PaymentGatewayException(
            $"PayPal could not {operation} (HTTP {status}).",
            status, null, null);
    }

    private static PaymentGatewayException Unprocessable(string operation) =>
        new PaymentGatewayException(
            $"PayPal's response to {operation} could not be processed.", null, "UNPROCESSABLE_RESPONSE");

    private static string FormatMoney(decimal amount) => amount.ToString("F2", CultureInfo.InvariantCulture);

    private static decimal? ParseMoney(Money? money) =>
        money?.Value is not null && decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static DateTimeOffset? ParseDate(string? value) =>
        value is not null && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;

    private static string FormatSearchDate(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
