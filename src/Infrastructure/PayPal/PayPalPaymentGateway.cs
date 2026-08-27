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
using Microsoft.Extensions.Logging;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// PayPal implementation of <see cref="IPaymentGateway"/> over the PayPalServerSdk client.
/// All SDK knowledge lives behind this boundary: every write forwards a PayPal-Request-Id
/// idempotency key, every call is bounded by a total-budget cancellation token, and every
/// failure is translated to a caller-safe <see cref="PaymentGatewayException"/> that carries
/// the provider status when one was received. Card numbers flow through here only — they are
/// never persisted and never logged.
/// </summary>
public class PayPalPaymentGateway : IPaymentGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private readonly PayPalServerSdkClient _client;
    private readonly ILogger<PayPalPaymentGateway> _logger;

    public PayPalPaymentGateway(PayPalServerSdkClient client, ILogger<PayPalPaymentGateway> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<GatewayAuthorization> AuthorizeAsync(
        int orderId, decimal amount, string currency, CardDetails? card, string? vaultTokenId,
        string idempotencyKey, CancellationToken ct)
    {
        if ((card == null) == string.IsNullOrEmpty(vaultTokenId))
        {
            throw new ArgumentException("Exactly one of card or vaultTokenId must be supplied.");
        }

        var cardRequest = card != null
            ? new CardRequest
            {
                Number = card.Number,
                Expiry = card.Expiry,
                SecurityCode = card.SecurityCode,
                Name = card.CardholderName,
                BillingAddress = MapAddress(card.BillingAddress)
            }
            : new CardRequest { VaultId = vaultTokenId };

        return await ExecuteAsync(async token =>
        {
            Order created;
            try
            {
                created = await _client.Orders.CreateOrder(
                    payPalMockResponse: null,
                    payPalRequestId: $"{idempotencyKey}-create",
                    payPalPartnerAttributionId: null,
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: new OrderRequest
                    {
                        Intent = CheckoutPaymentIntent.Authorize,
                        PurchaseUnits = new List<PurchaseUnitRequest>
                        {
                            new PurchaseUnitRequest
                            {
                                ReferenceId = $"eshop-order-{orderId}",
                                Amount = new AmountWithBreakdown
                                {
                                    CurrencyCode = currency,
                                    Value = FormatAmount(amount)
                                }
                            }
                        }
                    },
                    prefer: "return=representation",
                    ct: token);
            }
            catch (SdkException<CreateOrderError> ex)
            {
                throw TranslateCreateOrder(ex.Error);
            }

            if (created.Id == null)
            {
                throw new PaymentGatewayException("PayPal created the order without an id.");
            }

            OrderAuthorizeResponse authorized;
            try
            {
                authorized = await _client.Orders.AuthorizeOrder(
                    id: created.Id,
                    payPalMockResponse: null,
                    payPalRequestId: idempotencyKey,
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: new OrderAuthorizeRequest
                    {
                        PaymentSource = new OrderAuthorizeRequestPaymentSource { Card = cardRequest }
                    },
                    prefer: "return=representation",
                    ct: token);
            }
            catch (SdkException<AuthorizeOrderError> ex)
            {
                throw TranslateAuthorizeOrder(ex.Error);
            }

            if (authorized.Status == OrderStatus.PayerActionRequired)
            {
                throw new PaymentGatewayException(
                    "PayPal requires the shopper to approve this payment in a browser (3-D Secure); " +
                    "this integration does not support an approval round-trip.",
                    422, "PAYER_ACTION_REQUIRED");
            }

            var authorization = authorized.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
            if (authorization?.Id == null)
            {
                // Defensive: a minimal body may omit the payments collection — re-read the order.
                authorization = (await GetOrderInternal(created.Id, token))
                    .PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
            }

            if (authorization?.Id == null)
            {
                throw new PaymentGatewayException("PayPal authorized the order but returned no authorization id.");
            }

            return new GatewayAuthorization(
                created.Id,
                authorization.Id,
                authorization.Status?.Value ?? "UNKNOWN",
                ParseDate(authorization.ExpirationTime));
        }, "authorize", ct);
    }

    public async Task<GatewayAuthorizationStatus> GetAuthorizationAsync(string authorizationId, CancellationToken ct)
    {
        return await ExecuteAsync(async token =>
        {
            try
            {
                var authorization = await _client.Payments.GetAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    ct: token);

                return new GatewayAuthorizationStatus(
                    authorization.Id ?? authorizationId,
                    authorization.Status?.Value ?? "UNKNOWN",
                    ParseDate(authorization.ExpirationTime));
            }
            catch (SdkException<GetAuthorizedPaymentError> ex)
            {
                throw TranslateGetAuthorizedPayment(ex.Error);
            }
        }, "get authorization", ct);
    }

    public async Task<GatewayAuthorizationStatus> ReauthorizeAsync(
        string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken ct)
    {
        return await ExecuteAsync(async token =>
        {
            try
            {
                var renewed = await _client.Payments.ReauthorizePayment(
                    authorizationId: authorizationId,
                    payPalRequestId: idempotencyKey,
                    payPalAuthAssertion: null,
                    body: new ReauthorizeRequest
                    {
                        Amount = new Money { CurrencyCode = currency, Value = FormatAmount(amount) }
                    },
                    prefer: "return=representation",
                    ct: token);

                return new GatewayAuthorizationStatus(
                    renewed.Id ?? authorizationId,
                    renewed.Status?.Value ?? "UNKNOWN",
                    ParseDate(renewed.ExpirationTime));
            }
            catch (SdkException<ReauthorizePaymentError> ex)
            {
                throw TranslateReauthorize(ex.Error);
            }
        }, "reauthorize", ct);
    }

    public async Task<GatewayCapture> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken ct)
    {
        return await ExecuteAsync(async token =>
        {
            CapturedPayment capture;
            try
            {
                capture = await _client.Payments.CaptureAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalRequestId: idempotencyKey,
                    payPalAuthAssertion: null,
                    body: new CaptureRequest { FinalCapture = true },
                    prefer: "return=representation",
                    ct: token);
            }
            catch (SdkException<CaptureAuthorizedPaymentError> ex)
            {
                throw TranslateCapture(ex.Error);
            }

            if (capture.Id == null)
            {
                throw new PaymentGatewayException("PayPal captured the payment but returned no capture id.");
            }

            var breakdown = capture.SellerReceivableBreakdown;
            if (breakdown == null)
            {
                // Defensive: a minimal body may omit the breakdown — re-read the capture.
                breakdown = (await GetCaptureInternal(capture.Id, token)).SellerReceivableBreakdown;
            }

            return new GatewayCapture(
                capture.Id,
                capture.Status?.Value ?? "UNKNOWN",
                ParseMoney(capture.Amount) ?? 0m,
                ParseMoney(breakdown?.PaypalFee),
                ParseMoney(breakdown?.NetAmount));
        }, "capture", ct);
    }

    public async Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct)
    {
        await ExecuteAsync(async token =>
        {
            try
            {
                await _client.Payments.VoidPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    payPalRequestId: idempotencyKey,
                    prefer: "return=representation",
                    ct: token);
                return true;
            }
            catch (SdkException<VoidPaymentError> ex)
            {
                throw TranslateVoid(ex.Error);
            }
        }, "void", ct);
    }

    public async Task<GatewayRefund> RefundAsync(
        string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken ct)
    {
        return await ExecuteAsync(async token =>
        {
            try
            {
                var refund = await _client.Payments.RefundCapturedPayment(
                    captureId: captureId,
                    payPalMockResponse: null,
                    payPalRequestId: idempotencyKey,
                    payPalAuthAssertion: null,
                    body: amount.HasValue
                        ? new RefundRequest
                        {
                            Amount = new Money { CurrencyCode = currency, Value = FormatAmount(amount.Value) }
                        }
                        : null,
                    prefer: "return=representation",
                    ct: token);

                if (refund.Id == null)
                {
                    throw new PaymentGatewayException("PayPal refunded the capture but returned no refund id.");
                }

                return new GatewayRefund(
                    refund.Id,
                    refund.Status?.Value ?? "UNKNOWN",
                    ParseMoney(refund.Amount) ?? amount ?? 0m);
            }
            catch (SdkException<RefundCapturedPaymentError> ex)
            {
                throw TranslateRefund(ex.Error);
            }
        }, "refund", ct);
    }

    public async Task<GatewayVaultedCard> VaultCardAsync(
        string? customerId, string merchantCustomerId, CardDetails card, string idempotencyKey, CancellationToken ct)
    {
        return await ExecuteAsync(async token =>
        {
            try
            {
                var response = await _client.Vault.CreatePaymentToken(
                    payPalRequestId: idempotencyKey,
                    body: new PaymentTokenRequest
                    {
                        // Customer.Id is PayPal-generated (max 22 chars): omit it on the shopper's
                        // first card and read it back from the response; our own shopper key rides
                        // in MerchantCustomerId.
                        Customer = new Customer { Id = customerId, MerchantCustomerId = merchantCustomerId },
                        PaymentSource = new PaymentTokenRequestPaymentSource
                        {
                            Card = new PaymentTokenRequestCard
                            {
                                Number = card.Number,
                                Expiry = card.Expiry,
                                SecurityCode = card.SecurityCode,
                                Name = card.CardholderName,
                                BillingAddress = MapAddress(card.BillingAddress)
                            }
                        }
                    },
                    ct: token);

                if (response.Id == null)
                {
                    throw new PaymentGatewayException("PayPal vaulted the card but returned no payment token id.");
                }

                var vaultedCard = response.PaymentSource?.Card;
                return new GatewayVaultedCard(
                    response.Id,
                    response.Customer?.Id,
                    vaultedCard?.Brand?.Value,
                    vaultedCard?.LastDigits,
                    vaultedCard?.Expiry,
                    vaultedCard?.Name);
            }
            catch (SdkException<CreatePaymentTokenError> ex)
            {
                throw TranslateCreatePaymentToken(ex.Error);
            }
        }, "vault card", ct);
    }

    public async Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken ct)
    {
        await ExecuteAsync(async token =>
        {
            try
            {
                await _client.Vault.DeletePaymentToken(id: vaultTokenId, ct: token);
                return true;
            }
            catch (SdkException<DeletePaymentTokenError> ex)
            {
                throw TranslateDeletePaymentToken(ex.Error);
            }
        }, "delete vaulted card", ct);
    }

    public async Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        return await ExecuteAsync(async token =>
        {
            var all = new List<GatewayTransaction>();
            var page = 1;
            while (true)
            {
                SearchResponse response;
                try
                {
                    response = await _client.TransactionSearch.SearchTransactions(
                        startDate: FormatIso(from),
                        endDate: FormatIso(to),
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
                        ct: token);
                }
                catch (SdkException<RawError> ex)
                {
                    throw FromRaw("search transactions", ex.Error);
                }

                foreach (var detail in response.TransactionDetails ?? Enumerable.Empty<TransactionDetails>())
                {
                    var info = detail.TransactionInfo;
                    if (info?.TransactionId == null)
                    {
                        continue;
                    }

                    all.Add(new GatewayTransaction(
                        info.TransactionId,
                        info.TransactionEventCode,
                        info.TransactionStatus,
                        ParseMoney(info.TransactionAmount),
                        info.TransactionAmount?.CurrencyCode,
                        ParseMoney(info.FeeAmount),
                        ParseDate(info.TransactionInitiationDate),
                        ParseDate(info.TransactionUpdatedDate)));
                }

                if (response.TotalPages == null || page >= response.TotalPages)
                {
                    break;
                }
                page++;
            }

            return (IReadOnlyList<GatewayTransaction>)all;
        }, "search transactions", ct);
    }

    private async Task<Order> GetOrderInternal(string orderId, CancellationToken ct)
    {
        try
        {
            return await _client.Orders.GetOrder(
                id: orderId,
                fields: null,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                ct: ct);
        }
        catch (SdkException<GetOrderError> ex)
        {
            throw TranslateGetOrder(ex.Error);
        }
    }

    private async Task<CapturedPayment> GetCaptureInternal(string captureId, CancellationToken ct)
    {
        try
        {
            return await _client.Payments.GetCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                ct: ct);
        }
        catch (SdkException<GetCapturedPaymentError> ex)
        {
            throw TranslateGetCapturedPayment(ex.Error);
        }
    }

    private async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> call, string operation, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        try
        {
            return await call(cts.Token);
        }
        catch (PaymentGatewayException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            // A 2xx with a drifted body, or an error body that did not match the generated
            // error model — the outcome is unknown, so this is a provider-side failure.
            _logger.LogWarning(ex, "PayPal {Operation}: response could not be processed.", operation);
            throw new PaymentGatewayException($"PayPal returned a response that could not be processed during {operation}.", null, null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "PayPal {Operation}: provider unreachable.", operation);
            throw new PaymentGatewayException($"PayPal could not be reached during {operation}.", null, null, ex);
        }
    }

    private static string FormatAmount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string FormatIso(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static decimal? ParseMoney(Money? money) =>
        money?.Value != null && decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;

    private static Address? MapAddress(BillingAddressDetails? address) =>
        address == null
            ? null
            : new Address
            {
                CountryCode = address.CountryCode,
                AddressLine1 = address.AddressLine1,
                AddressLine2 = address.AddressLine2,
                AdminArea2 = address.City,
                AdminArea1 = address.State,
                PostalCode = address.PostalCode
            };

    // --- Error translation: one translator per operation, one branch per TryGet* accessor ---
    // (TryGetRawError always last; it only fires for statuses with no more-specific accessor.)

    private PaymentGatewayException TranslateCreateOrder(CreateOrderError error)
    {
        if (error.TryGetError(out var e)) return FromError("create order", e);
        if (error.TryGetRawError(out var raw)) return FromRaw("create order", raw);
        return Unknown("create order");
    }

    private PaymentGatewayException TranslateAuthorizeOrder(AuthorizeOrderError error)
    {
        if (error.TryGetError(out var e)) return FromError("authorize order", e);
        if (error.TryGetRawError(out var raw)) return FromRaw("authorize order", raw);
        return Unknown("authorize order");
    }

    private PaymentGatewayException TranslateGetOrder(GetOrderError error)
    {
        if (error.TryGetError(out var e)) return FromError("get order", e);
        if (error.TryGetRawError(out var raw)) return FromRaw("get order", raw);
        return Unknown("get order");
    }

    private PaymentGatewayException TranslateCapture(CaptureAuthorizedPaymentError error)
    {
        if (error.TryGetError(out var e)) return FromError("capture payment", e);
        if (error.TryGetNoContent(out var noContent)) return FromRaw("capture payment", noContent);
        if (error.TryGetRawError(out var raw)) return FromRaw("capture payment", raw);
        return Unknown("capture payment");
    }

    private PaymentGatewayException TranslateReauthorize(ReauthorizePaymentError error)
    {
        if (error.TryGetError(out var e)) return FromError("reauthorize payment", e);
        if (error.TryGetNoContent(out var noContent)) return FromRaw("reauthorize payment", noContent);
        if (error.TryGetRawError(out var raw)) return FromRaw("reauthorize payment", raw);
        return Unknown("reauthorize payment");
    }

    private PaymentGatewayException TranslateVoid(VoidPaymentError error)
    {
        if (error.TryGetError(out var e)) return FromError("void authorization", e);
        if (error.TryGetNoContent(out var noContent)) return FromRaw("void authorization", noContent);
        if (error.TryGetRawError(out var raw)) return FromRaw("void authorization", raw);
        return Unknown("void authorization");
    }

    private PaymentGatewayException TranslateRefund(RefundCapturedPaymentError error)
    {
        if (error.TryGetError(out var e)) return FromError("refund payment", e);
        if (error.TryGetNoContent(out var noContent)) return FromRaw("refund payment", noContent);
        if (error.TryGetRawError(out var raw)) return FromRaw("refund payment", raw);
        return Unknown("refund payment");
    }

    private PaymentGatewayException TranslateGetAuthorizedPayment(GetAuthorizedPaymentError error)
    {
        if (error.TryGetError(out var e)) return FromError("get authorization", e);
        if (error.TryGetNoContent(out var noContent)) return FromRaw("get authorization", noContent);
        if (error.TryGetRawError(out var raw)) return FromRaw("get authorization", raw);
        return Unknown("get authorization");
    }

    private PaymentGatewayException TranslateGetCapturedPayment(GetCapturedPaymentError error)
    {
        if (error.TryGetError(out var e)) return FromError("get capture", e);
        if (error.TryGetNoContent(out var noContent)) return FromRaw("get capture", noContent);
        if (error.TryGetRawError(out var raw)) return FromRaw("get capture", raw);
        return Unknown("get capture");
    }

    private PaymentGatewayException TranslateCreatePaymentToken(CreatePaymentTokenError error)
    {
        if (error.TryGetError1(out var e)) return FromError("vault card", e);
        if (error.TryGetRawError(out var raw)) return FromRaw("vault card", raw);
        return Unknown("vault card");
    }

    private PaymentGatewayException TranslateDeletePaymentToken(DeletePaymentTokenError error)
    {
        if (error.TryGetError1(out var e)) return FromError("delete vaulted card", e);
        if (error.TryGetRawError(out var raw)) return FromRaw("delete vaulted card", raw);
        return Unknown("delete vaulted card");
    }

    private PaymentGatewayException FromError(string operation, Error error)
    {
        var issues = error.Details?
            .Select(d => d.Issue)
            .Where(i => !string.IsNullOrEmpty(i))
            .ToList() ?? new List<string>();
        var detail = issues.Count > 0 ? $" ({string.Join(", ", issues)})" : string.Empty;
        _logger.LogWarning("PayPal {Operation} rejected: {Name} {Issues} (debug id {DebugId}).",
            operation, error.Name, string.Join(", ", issues), error.DebugId);
        return new PaymentGatewayException(
            $"PayPal could not {operation}: {error.Name}{detail}.",
            StatusFromName(error.Name), error.Name);
    }

    private PaymentGatewayException FromError(string operation, Error1 error)
    {
        var issues = error.Details?
            .Select(d => d.Issue)
            .Where(i => !string.IsNullOrEmpty(i))
            .ToList() ?? new List<string>();
        var detail = issues.Count > 0 ? $" ({string.Join(", ", issues)})" : string.Empty;
        _logger.LogWarning("PayPal {Operation} rejected: {Name} {Issues} (debug id {DebugId}).",
            operation, error.Name, string.Join(", ", issues), error.DebugId);
        return new PaymentGatewayException(
            $"PayPal could not {operation}: {error.Name}{detail}.",
            StatusFromName(error.Name), error.Name);
    }

    private PaymentGatewayException FromRaw(string operation, RawError raw)
    {
        var status = (int)raw.StatusCode;
        _logger.LogWarning("PayPal {Operation} failed with status {Status}: {Body}.",
            operation, status, raw.ReadAsString());
        return new PaymentGatewayException(
            $"PayPal could not {operation} (provider status {status}).", status, null);
    }

    private static PaymentGatewayException Unknown(string operation) =>
        new($"PayPal could not {operation}.");

    private static int? StatusFromName(string? name) => name switch
    {
        "INVALID_REQUEST" => 400,
        "AUTHENTICATION_FAILURE" => 401,
        "NOT_AUTHORIZED" or "PERMISSION_DENIED" => 403,
        "RESOURCE_NOT_FOUND" or "NOT_FOUND" => 404,
        "RESOURCE_CONFLICT" or "DUPLICATE_REQUEST_ID" => 409,
        "UNPROCESSABLE_ENTITY" => 422,
        _ => null
    };
}
