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
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using PayPalAddress = PayPalServerSdk.Models.Address;
using PayPalOrderStatus = PayPalServerSdk.Models.Enums.OrderStatus;

namespace Microsoft.eShopWeb.PublicApi.Services;

/// <summary>
/// PayPal implementation of the payment gateway. Full card details pass through to PayPal
/// and are never logged or persisted. Every call is bounded by a total-call budget and all
/// SDK failures are translated into caller-safe <see cref="PaymentGatewayException"/>s.
/// </summary>
public class PayPalPaymentGateway : IPaymentGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private readonly PayPalServerSdkClient _client;
    private readonly IAppLogger<PayPalPaymentGateway> _logger;

    public PayPalPaymentGateway(PayPalServerSdkClient client, IAppLogger<PayPalPaymentGateway> logger)
    {
        _client = client;
        _logger = logger;
    }

    public Task<GatewayAuthorization> AuthorizeWithCardAsync(string reference, decimal amount, string currency,
        GatewayCardDetails card, string idempotencyKey, CancellationToken ct = default)
    {
        var cardRequest = new CardRequest
        {
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            Name = card.Name,
            BillingAddress = MapAddress(card.BillingAddress)
        };
        return AuthorizeAsync(reference, amount, currency, cardRequest, idempotencyKey, ct);
    }

    public Task<GatewayAuthorization> AuthorizeWithSavedCardAsync(string reference, decimal amount, string currency,
        string vaultTokenId, string idempotencyKey, CancellationToken ct = default)
    {
        var cardRequest = new CardRequest
        {
            VaultId = vaultTokenId
        };
        return AuthorizeAsync(reference, amount, currency, cardRequest, idempotencyKey, ct);
    }

    private async Task<GatewayAuthorization> AuthorizeAsync(string reference, decimal amount, string currency,
        CardRequest card, string idempotencyKey, CancellationToken ct)
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
                        Value = FormatAmount(amount)
                    },
                    ReferenceId = reference,
                    CustomId = reference,
                    // The merchant account requires globally unique invoice ids; the stable
                    // reference stays on custom_id for reconciliation matching.
                    InvoiceId = $"{reference}-{Guid.NewGuid():N}"
                }
            },
            PaymentSource = new PaymentSource { Card = card }
        };

        try
        {
            var order = await Bounded(ct, token => _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                requestOptions: null,
                ct: token));

            if (order.Status == PayPalOrderStatus.PayerActionRequired)
            {
                _logger.LogWarning($"PayPal order {order.Id} requires buyer action (3DS/SCA); not supported by this integration.");
                throw new PaymentGatewayException(
                    "PayPal requires the shopper to approve this payment in a browser (3-D Secure), which this integration does not support.",
                    422);
            }

            var authorization = order.PurchaseUnits?
                .SelectMany(pu => pu.Payments?.Authorizations ?? Enumerable.Empty<AuthorizationWithAdditionalData>())
                .FirstOrDefault();

            if (order.Id is null || authorization?.Id is null)
            {
                _logger.LogWarning($"PayPal order {order.Id} returned without an authorization.");
                throw new PaymentGatewayException("PayPal did not return an authorization for the payment.", 502);
            }

            return new GatewayAuthorization(
                order.Id,
                authorization.Id,
                authorization.Status?.Value ?? "UNKNOWN",
                ParseTime(authorization.ExpirationTime),
                ParseAmount(authorization.Amount) ?? amount,
                PayerActionRequired: false);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw Translate("create order", ex.Error.TryGetError, ex.Error.TryGetRawError);
        }
        catch (Exception ex) when (IsTransportOrParseFailure(ex, ct))
        {
            throw TranslateBoundary("create order", ex);
        }
    }

    public async Task<GatewayAuthorizationState> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default)
    {
        try
        {
            var authorization = await Bounded(ct, token => _client.Payments.GetAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                requestOptions: null,
                ct: token));

            return new GatewayAuthorizationState(
                authorization.Id ?? authorizationId,
                authorization.Status?.Value ?? "UNKNOWN",
                ParseTime(authorization.ExpirationTime),
                ParseAmount(authorization.Amount) ?? 0m);
        }
        catch (SdkException<GetAuthorizedPaymentError> ex)
        {
            throw Translate("get authorization",
                ex.Error.TryGetError,
                ex.Error.TryGetNoContent,
                ex.Error.TryGetRawError);
        }
        catch (Exception ex) when (IsTransportOrParseFailure(ex, ct))
        {
            throw TranslateBoundary("get authorization", ex);
        }
    }

    public async Task<GatewayAuthorizationState> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            var authorization = await Bounded(ct, token => _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest
                {
                    Amount = new Money { CurrencyCode = currency, Value = FormatAmount(amount) }
                },
                prefer: "return=representation",
                requestOptions: null,
                ct: token));

            return new GatewayAuthorizationState(
                authorization.Id ?? authorizationId,
                authorization.Status?.Value ?? "UNKNOWN",
                ParseTime(authorization.ExpirationTime),
                ParseAmount(authorization.Amount) ?? amount);
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            throw Translate("reauthorize payment",
                ex.Error.TryGetError,
                ex.Error.TryGetNoContent,
                ex.Error.TryGetRawError);
        }
        catch (Exception ex) when (IsTransportOrParseFailure(ex, ct))
        {
            throw TranslateBoundary("reauthorize payment", ex);
        }
    }

    public async Task<GatewayCapture> CaptureAuthorizationAsync(string authorizationId, decimal amount,
        string currency, string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            var capture = await Bounded(ct, token => _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: new CaptureRequest
                {
                    Amount = new Money { CurrencyCode = currency, Value = FormatAmount(amount) },
                    FinalCapture = true
                },
                prefer: "return=representation",
                requestOptions: null,
                ct: token));

            var breakdown = capture.SellerReceivableBreakdown;
            return new GatewayCapture(
                capture.Id ?? string.Empty,
                capture.Status?.Value ?? "UNKNOWN",
                ParseAmount(breakdown?.GrossAmount) ?? ParseAmount(capture.Amount) ?? amount,
                ParseAmount(breakdown?.PaypalFee),
                ParseAmount(breakdown?.NetAmount));
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            throw Translate("capture payment",
                ex.Error.TryGetError,
                ex.Error.TryGetNoContent,
                ex.Error.TryGetRawError);
        }
        catch (Exception ex) when (IsTransportOrParseFailure(ex, ct))
        {
            throw TranslateBoundary("capture payment", ex);
        }
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            await Bounded(ct, async token =>
            {
                await _client.Payments.VoidPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    payPalRequestId: idempotencyKey,
                    prefer: "return=representation",
                    requestOptions: null,
                    ct: token);
                return true;
            });
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            throw Translate("void authorization",
                ex.Error.TryGetError,
                ex.Error.TryGetNoContent,
                ex.Error.TryGetRawError);
        }
        catch (Exception ex) when (IsTransportOrParseFailure(ex, ct))
        {
            throw TranslateBoundary("void authorization", ex);
        }
    }

    public async Task<GatewayRefund> RefundCaptureAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            var refund = await Bounded(ct, token => _client.Payments.RefundCapturedPayment(
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
                requestOptions: null,
                ct: token));

            return new GatewayRefund(
                refund.Id ?? string.Empty,
                refund.Status?.Value ?? "UNKNOWN",
                ParseAmount(refund.Amount) ?? amount ?? 0m);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            throw Translate("refund capture",
                ex.Error.TryGetError,
                ex.Error.TryGetNoContent,
                ex.Error.TryGetRawError);
        }
        catch (Exception ex) when (IsTransportOrParseFailure(ex, ct))
        {
            throw TranslateBoundary("refund capture", ex);
        }
    }

    public async Task<GatewaySavedCard> SaveCardAsync(string merchantCustomerId, GatewayCardDetails card,
        string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            var response = await Bounded(ct, token => _client.Vault.CreatePaymentToken(
                payPalRequestId: idempotencyKey,
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
                            BillingAddress = MapAddress(card.BillingAddress)
                        }
                    },
                    Customer = new Customer { MerchantCustomerId = merchantCustomerId }
                },
                requestOptions: null,
                ct: token));

            if (response.Id is null)
            {
                throw new PaymentGatewayException("PayPal did not return a payment token for the card.", 502);
            }

            var vaultedCard = response.PaymentSource?.Card;
            return new GatewaySavedCard(
                response.Id,
                response.Customer?.Id,
                vaultedCard?.Brand?.Value,
                vaultedCard?.LastDigits,
                vaultedCard?.Expiry,
                vaultedCard?.Name);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            throw TranslateVault("save card", ex.Error.TryGetError1, ex.Error.TryGetRawError);
        }
        catch (Exception ex) when (IsTransportOrParseFailure(ex, ct))
        {
            throw TranslateBoundary("save card", ex);
        }
    }

    public async Task DeleteSavedCardAsync(string paymentTokenId, CancellationToken ct = default)
    {
        try
        {
            await Bounded(ct, async token =>
            {
                await _client.Vault.DeletePaymentToken(
                    id: paymentTokenId,
                    requestOptions: null,
                    ct: token);
                return true;
            });
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            throw TranslateVault("delete saved card", ex.Error.TryGetError1, ex.Error.TryGetRawError);
        }
        catch (Exception ex) when (IsTransportOrParseFailure(ex, ct))
        {
            throw TranslateBoundary("delete saved card", ex);
        }
    }

    public async Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken ct = default)
    {
        var transactions = new List<GatewayTransaction>();
        var maxWindow = TimeSpan.FromDays(31);

        for (var windowStart = from; windowStart < to; windowStart = windowStart + maxWindow)
        {
            var windowEnd = windowStart + maxWindow < to ? windowStart + maxWindow : to;
            var page = 1;
            while (true)
            {
                SearchResponse response;
                try
                {
                    response = await Bounded(ct, token => _client.TransactionSearch.SearchTransactions(
                        startDate: FormatTimestamp(windowStart),
                        endDate: FormatTimestamp(windowEnd),
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
                        ct: token));
                }
                catch (SdkException<RawError> ex)
                {
                    throw new PaymentGatewayException(
                        $"PayPal transaction search failed with status {(int)ex.Error.StatusCode}.",
                        (int)ex.Error.StatusCode, ex);
                }
                catch (Exception ex) when (IsTransportOrParseFailure(ex, ct))
                {
                    throw TranslateBoundary("search transactions", ex);
                }

                foreach (var detail in response.TransactionDetails ?? Enumerable.Empty<TransactionDetails>())
                {
                    var info = detail.TransactionInfo;
                    if (info?.TransactionId is null) continue;

                    transactions.Add(new GatewayTransaction(
                        info.TransactionId,
                        info.PaypalReferenceId,
                        info.PaypalReferenceIdType?.Value,
                        ParseAmount(info.TransactionAmount),
                        ParseAmount(info.FeeAmount),
                        info.TransactionStatus,
                        ParseTime(info.TransactionInitiationDate),
                        info.InvoiceId,
                        info.CustomField));
                }

                var totalPages = response.TotalPages ?? 1;
                if (page >= totalPages) break;
                page++;
            }
        }

        return transactions;
    }

    private async Task<T> Bounded<T>(CancellationToken ct, Func<CancellationToken, Task<T>> call)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private static PayPalAddress? MapAddress(GatewayBillingAddress? address)
    {
        if (address is null) return null;
        return new PayPalAddress
        {
            CountryCode = address.CountryCode,
            AddressLine1 = address.AddressLine1,
            AddressLine2 = address.AddressLine2,
            AdminArea2 = address.City,
            AdminArea1 = address.State,
            PostalCode = address.PostalCode
        };
    }

    private static string FormatAmount(decimal amount)
        => amount.ToString("F2", CultureInfo.InvariantCulture);

    private static string FormatTimestamp(DateTimeOffset value)
        => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static decimal? ParseAmount(Money? money)
        => money is not null
           && decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static DateTimeOffset? ParseTime(string? value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto)
            ? dto
            : null;

    private static bool IsTransportOrParseFailure(Exception ex, CancellationToken ct)
        => ex is HttpRequestException or JsonException
           || (ex is TaskCanceledException && !ct.IsCancellationRequested);

    private static PaymentGatewayException TranslateBoundary(string operation, Exception ex)
        => ex is JsonException
            ? new PaymentGatewayException(
                $"PayPal {operation}: the provider returned a response that could not be processed.", null, ex)
            : new PaymentGatewayException(
                $"PayPal {operation}: the payment provider could not be reached.", null, ex);

    private delegate bool TryGet<T>(out T value);

    // Typed PayPal errors map to 4xx statuses on every operation used here; the exact status is
    // not carried on the typed model, so 400 stands in for "the caller can act on this".
    private static PaymentGatewayException Translate(string operation,
        TryGet<Error> tryGetError,
        TryGet<RawError> tryGetNoContent,
        TryGet<RawError> tryGetRawError)
    {
        if (tryGetError(out var error))
        {
            return new PaymentGatewayException(
                $"PayPal {operation} was rejected: {error.Name} - {error.Message}{DescribeDetails(error.Details)} (debug id {error.DebugId}).", 400);
        }
        if (tryGetNoContent(out var noContent))
        {
            return new PaymentGatewayException(
                $"PayPal {operation} failed with status {(int)noContent.StatusCode}.", (int)noContent.StatusCode);
        }
        if (tryGetRawError(out var raw))
        {
            return new PaymentGatewayException(
                $"PayPal {operation} failed with status {(int)raw.StatusCode}.", (int)raw.StatusCode);
        }
        return new PaymentGatewayException($"PayPal {operation} failed.");
    }

    private static PaymentGatewayException Translate(string operation,
        TryGet<Error> tryGetError,
        TryGet<RawError> tryGetRawError)
    {
        if (tryGetError(out var error))
        {
            return new PaymentGatewayException(
                $"PayPal {operation} was rejected: {error.Name} - {error.Message}{DescribeDetails(error.Details)} (debug id {error.DebugId}).", 400);
        }
        if (tryGetRawError(out var raw))
        {
            return new PaymentGatewayException(
                $"PayPal {operation} failed with status {(int)raw.StatusCode}.", (int)raw.StatusCode);
        }
        return new PaymentGatewayException($"PayPal {operation} failed.");
    }

    private static string DescribeDetails(IReadOnlyList<ErrorDetails>? details)
        => details is null || details.Count == 0
            ? string.Empty
            : " [" + string.Join("; ", details.Select(d => $"{d.Issue}: {d.Description} (field {d.Field}, value '{d.Value}')")) + "]";

    private static PaymentGatewayException TranslateVault(string operation,
        TryGet<Error1> tryGetError1,
        TryGet<RawError> tryGetRawError)
    {
        if (tryGetError1(out var error))
        {
            return new PaymentGatewayException(
                $"PayPal {operation} was rejected: {error.Name} - {error.Message} (debug id {error.DebugId}).", 400);
        }
        if (tryGetRawError(out var raw))
        {
            return new PaymentGatewayException(
                $"PayPal {operation} failed with status {(int)raw.StatusCode}.", (int)raw.StatusCode);
        }
        return new PaymentGatewayException($"PayPal {operation} failed.");
    }
}
