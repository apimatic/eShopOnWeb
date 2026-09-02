using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using PayPalAddress = PayPalServerSdk.Models.Address;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// PayPal implementation of the payment gateway. Every call is bounded by a total-budget
/// cancellation token and translated into PaymentGatewayException on failure, so callers
/// only ever see one failure type. Card data flows through here and is never logged.
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

    public async Task<GatewayAuthorizationResult> AuthorizeAsync(string invoiceId, decimal amount, string currency,
        GatewayCard? card, string? vaultId, string idempotencyKey, CancellationToken ct)
    {
        var cardRequest = BuildCardRequest(card, vaultId);
        var orderRequest = new OrderRequest
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
                    CustomId = invoiceId,
                    InvoiceId = invoiceId,
                    Description = $"eShopOnWeb order {invoiceId}"
                }
            },
            PaymentSource = cardRequest is null ? null : new PaymentSource { Card = cardRequest }
        };

        try
        {
            var order = await Bounded(token => _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: orderRequest,
                prefer: "return=representation",
                requestOptions: null,
                ct: token), ct);

            // With a card payment source and no payer interaction required, PayPal may
            // authorize the order already on create; only call authorize when it did not.
            var auth = order.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
            if (auth is null)
            {
                var authorization = await Bounded(token => _client.Orders.AuthorizeOrder(
                    id: order.Id!,
                    payPalMockResponse: null,
                    payPalRequestId: idempotencyKey + "-auth",
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: null,
                    prefer: "return=representation",
                    requestOptions: null,
                    ct: token), ct);
                auth = authorization.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
            }
            if (auth?.Id is null)
            {
                throw new PaymentGatewayException("PayPal did not return an authorization for the payment.");
            }
            if (auth.Status == AuthorizationStatus.Denied)
            {
                throw new PaymentGatewayException(422, "The card was declined by PayPal.");
            }
            return new GatewayAuthorizationResult(order.Id!, auth.Id, auth.Status?.Value ?? "UNKNOWN", ParseDate(auth.ExpirationTime));
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw Translate("create the PayPal order", ex.Error);
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            throw Translate("authorize the payment", ex.Error);
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or TaskCanceledException)
        {
            throw TranslateTransport("authorize the payment", ex);
        }
    }

    public async Task<GatewayAuthorizationStatus> GetAuthorizationAsync(string authorizationId, CancellationToken ct)
    {
        try
        {
            var auth = await Bounded(token => _client.Payments.GetAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                requestOptions: null,
                ct: token), ct);
            return new GatewayAuthorizationStatus(auth.Id ?? authorizationId, auth.Status?.Value ?? "UNKNOWN", ParseDate(auth.ExpirationTime));
        }
        catch (SdkException<GetAuthorizedPaymentError> ex)
        {
            throw Translate("read the PayPal authorization", ex.Error);
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or TaskCanceledException)
        {
            throw TranslateTransport("read the PayPal authorization", ex);
        }
    }

    public async Task<GatewayAuthorizationStatus> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken ct)
    {
        try
        {
            var auth = await Bounded(token => _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest
                {
                    Amount = new Money { CurrencyCode = currency, Value = FormatAmount(amount) }
                },
                prefer: "return=representation",
                requestOptions: null,
                ct: token), ct);
            return new GatewayAuthorizationStatus(auth.Id ?? authorizationId, auth.Status?.Value ?? "UNKNOWN", ParseDate(auth.ExpirationTime));
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            throw Translate("renew the PayPal authorization", ex.Error);
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or TaskCanceledException)
        {
            throw TranslateTransport("renew the PayPal authorization", ex);
        }
    }

    public async Task<GatewayCaptureResult> CaptureAsync(string authorizationId, string invoiceId, string idempotencyKey, CancellationToken ct)
    {
        try
        {
            var capture = await Bounded(token => _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: new CaptureRequest { InvoiceId = invoiceId, FinalCapture = true },
                prefer: "return=representation",
                requestOptions: null,
                ct: token), ct);

            var breakdown = capture.SellerReceivableBreakdown;
            var gross = ParseMoney(breakdown?.GrossAmount) ?? ParseMoney(capture.Amount) ?? 0m;
            return new GatewayCaptureResult(
                capture.Id ?? string.Empty,
                capture.Status?.Value ?? "UNKNOWN",
                gross,
                ParseMoney(breakdown?.PaypalFee),
                ParseMoney(breakdown?.NetAmount));
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            throw Translate("capture the payment", ex.Error);
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or TaskCanceledException)
        {
            throw TranslateTransport("capture the payment", ex);
        }
    }

    public async Task<string> VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct)
    {
        try
        {
            var auth = await Bounded(token => _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: idempotencyKey,
                prefer: "return=representation",
                requestOptions: null,
                ct: token), ct);
            return auth.Status?.Value ?? "VOIDED";
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            throw Translate("release the held funds", ex.Error);
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or TaskCanceledException)
        {
            throw TranslateTransport("release the held funds", ex);
        }
    }

    public async Task<GatewayRefundResult> RefundAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, CancellationToken ct)
    {
        // PayPal treats an empty payload as a full refund; an amount makes it partial.
        var body = amount.HasValue
            ? new RefundRequest { Amount = new Money { CurrencyCode = currency, Value = FormatAmount(amount.Value) } }
            : new RefundRequest();

        try
        {
            var refund = await Bounded(token => _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                requestOptions: null,
                ct: token), ct);
            return new GatewayRefundResult(
                refund.Id ?? string.Empty,
                refund.Status?.Value ?? "UNKNOWN",
                ParseMoney(refund.Amount) ?? amount ?? 0m);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            throw Translate("refund the payment", ex.Error);
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or TaskCanceledException)
        {
            throw TranslateTransport("refund the payment", ex);
        }
    }

    public async Task<GatewaySavedCardResult> SaveCardAsync(string customerId, GatewayCard card, string idempotencyKey, CancellationToken ct)
    {
        var request = new PaymentTokenRequest
        {
            // Our user id goes in MerchantCustomerId; Customer.Id is PayPal-generated and
            // rejects values like email addresses at creation.
            Customer = new Customer { MerchantCustomerId = customerId },
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    Name = card.Name,
                    BillingAddress = BuildAddress(card)
                }
            }
        };

        try
        {
            var token = await Bounded(t => _client.Vault.CreatePaymentToken(
                payPalRequestId: idempotencyKey,
                body: request,
                requestOptions: null,
                ct: t), ct);

            if (token.Id is null)
            {
                throw new PaymentGatewayException("PayPal did not return a vaulted card token.");
            }
            var cardEntity = token.PaymentSource?.Card;
            return new GatewaySavedCardResult(
                token.Id,
                cardEntity?.Brand?.Value ?? "CARD",
                cardEntity?.LastDigits ?? (card.Number.Length >= 4 ? card.Number[^4..] : "****"),
                cardEntity?.Expiry ?? card.Expiry);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            throw TranslateVault("save the card", ex.Error);
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or TaskCanceledException)
        {
            throw TranslateTransport("save the card", ex);
        }
    }

    public async Task DeleteCardAsync(string vaultTokenId, CancellationToken ct)
    {
        try
        {
            await Bounded(t => _client.Vault.DeletePaymentToken(
                id: vaultTokenId,
                requestOptions: null,
                ct: t), ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            throw TranslateVault("delete the saved card", ex.Error);
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or TaskCanceledException)
        {
            throw TranslateTransport("delete the saved card", ex);
        }
    }

    public async Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var results = new List<GatewayTransaction>();
        var page = 1;
        var totalPages = 1;

        try
        {
            do
            {
                var response = await Bounded(token => _client.TransactionSearch.SearchTransactions(
                    startDate: from.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
                    endDate: to.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
                    transactionId: null,
                    transactionType: null,
                    transactionStatus: null,
                    transactionAmount: null,
                    transactionCurrency: null,
                    paymentInstrumentType: null,
                    storeId: null,
                    terminalId: null,
                    fields: "transaction_info",
                    balanceAffectingRecordsOnly: null,
                    pageSize: 100,
                    page: page,
                    requestOptions: null,
                    ct: token), ct);

                totalPages = response.TotalPages ?? 1;
                foreach (var detail in response.TransactionDetails ?? Enumerable.Empty<TransactionDetails>())
                {
                    var info = detail.TransactionInfo;
                    if (info is null)
                    {
                        continue;
                    }
                    results.Add(new GatewayTransaction(
                        info.TransactionId ?? string.Empty,
                        info.TransactionInitiationDate,
                        ParseMoney(info.TransactionAmount),
                        info.TransactionAmount?.CurrencyCode,
                        ParseMoney(info.FeeAmount),
                        info.TransactionStatus,
                        info.InvoiceId,
                        info.CustomField));
                }
                page++;
            }
            while (page <= totalPages);
        }
        catch (SdkException<RawError> ex)
        {
            throw RawFailure("search PayPal transactions", ex.Error);
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or TaskCanceledException)
        {
            throw TranslateTransport("search PayPal transactions", ex);
        }

        return results;
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private async Task Bounded(Func<CancellationToken, Task> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        await call(cts.Token);
    }

    private static CardRequest? BuildCardRequest(GatewayCard? card, string? vaultId)
    {
        if (card is not null)
        {
            return new CardRequest
            {
                Number = card.Number,
                Expiry = card.Expiry,
                SecurityCode = card.SecurityCode,
                Name = card.Name,
                BillingAddress = BuildAddress(card)
            };
        }
        if (vaultId is not null)
        {
            return new CardRequest
            {
                VaultId = vaultId,
                StoredCredential = new CardStoredCredential
                {
                    PaymentInitiator = PaymentInitiator.Merchant,
                    PaymentType = StoredPaymentSourcePaymentType.Unscheduled,
                    Usage = StoredPaymentSourceUsageType.Subsequent
                }
            };
        }
        return null;
    }

    private static PayPalAddress? BuildAddress(GatewayCard card)
    {
        if (card.AddressLine1 is null && card.City is null && card.PostalCode is null)
        {
            return null;
        }
        return new PayPalAddress
        {
            CountryCode = card.CountryCode,
            AddressLine1 = card.AddressLine1,
            AdminArea2 = card.City,
            AdminArea1 = card.State,
            PostalCode = card.PostalCode
        };
    }

    private static string FormatAmount(decimal amount) => amount.ToString("F2", CultureInfo.InvariantCulture);

    private static decimal? ParseMoney(Money? money) =>
        money?.Value is null ? null : decimal.Parse(money.Value, CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseDate(string? value) =>
        value is null ? null : DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);

    private PaymentGatewayException Rejection(string action, string? name, string? message, IEnumerable<ErrorDetails>? details)
    {
        var issues = details?.Select(d =>
            d.Field is null ? d.Issue : $"{d.Issue} (field: {d.Field}, value: {d.Value}) {d.Description}");
        // The SDK's typed error branch does not expose the HTTP status, so map from PayPal's
        // error name: caller-actionable rejections stay 4xx; our own credential/permission
        // failures are server-side and surface as no-status (502 at the boundary).
        var status = MapProviderStatus(name);
        var detail = issues is null ? message : $"{message} [{string.Join(", ", issues)}]";
        _logger.LogWarning("PayPal rejected attempt to {Action}: {Name} {Detail}", action, name, detail);
        return new PaymentGatewayException(status, $"PayPal could not {action}: {detail}");
    }

    private static int? MapProviderStatus(string? errorName) => errorName switch
    {
        null => null,
        var n when n.Contains("NOT_FOUND") => 404,
        var n when n.Contains("DUPLICATE") || n.Contains("CONFLICT") => 409,
        var n when n.Contains("UNAUTHORIZED") || n.Contains("AUTHENTICATION")
            || n.Contains("NOT_AUTHORIZED") || n.Contains("PERMISSION") || n.Contains("FORBIDDEN") => null,
        _ => 422
    };

    private PaymentGatewayException RawFailure(string action, RawError raw)
    {
        _logger.LogWarning("PayPal failure attempting to {Action}: HTTP {Status}", action, (int)raw.StatusCode);
        return new PaymentGatewayException((int)raw.StatusCode, $"PayPal could not {action} (HTTP {(int)raw.StatusCode}).");
    }

    private PaymentGatewayException TranslateTransport(string action, Exception ex)
    {
        if (ex is JsonException)
        {
            _logger.LogError(ex, "PayPal returned an unreadable response while trying to {Action}.", action);
            return new PaymentGatewayException($"PayPal returned a response that could not be processed while trying to {action}.", ex);
        }
        _logger.LogError(ex, "PayPal was unreachable while trying to {Action}.", action);
        return new PaymentGatewayException($"PayPal could not be reached while trying to {action}; the operation may not have completed.", ex);
    }

    private PaymentGatewayException Translate(string action, CreateOrderError error)
    {
        if (error.TryGetError(out var err)) return Rejection(action, err.Name, err.Message, err.Details);
        if (error.TryGetRawError(out var raw)) return RawFailure(action, raw);
        return new PaymentGatewayException($"PayPal could not {action}.");
    }

    private PaymentGatewayException Translate(string action, AuthorizeOrderError error)
    {
        if (error.TryGetError(out var err)) return Rejection(action, err.Name, err.Message, err.Details);
        if (error.TryGetRawError(out var raw)) return RawFailure(action, raw);
        return new PaymentGatewayException($"PayPal could not {action}.");
    }

    private PaymentGatewayException Translate(string action, GetAuthorizedPaymentError error)
    {
        if (error.TryGetError(out var err)) return Rejection(action, err.Name, err.Message, err.Details);
        if (error.TryGetNoContent(out var raw)) return RawFailure(action, raw);
        if (error.TryGetRawError(out var fallback)) return RawFailure(action, fallback);
        return new PaymentGatewayException($"PayPal could not {action}.");
    }

    private PaymentGatewayException Translate(string action, ReauthorizePaymentError error)
    {
        if (error.TryGetError(out var err)) return Rejection(action, err.Name, err.Message, err.Details);
        if (error.TryGetNoContent(out var raw)) return RawFailure(action, raw);
        if (error.TryGetRawError(out var fallback)) return RawFailure(action, fallback);
        return new PaymentGatewayException($"PayPal could not {action}.");
    }

    private PaymentGatewayException Translate(string action, CaptureAuthorizedPaymentError error)
    {
        if (error.TryGetError(out var err)) return Rejection(action, err.Name, err.Message, err.Details);
        if (error.TryGetNoContent(out var raw)) return RawFailure(action, raw);
        if (error.TryGetRawError(out var fallback)) return RawFailure(action, fallback);
        return new PaymentGatewayException($"PayPal could not {action}.");
    }

    private PaymentGatewayException Translate(string action, VoidPaymentError error)
    {
        if (error.TryGetError(out var err)) return Rejection(action, err.Name, err.Message, err.Details);
        if (error.TryGetNoContent(out var raw)) return RawFailure(action, raw);
        if (error.TryGetRawError(out var fallback)) return RawFailure(action, fallback);
        return new PaymentGatewayException($"PayPal could not {action}.");
    }

    private PaymentGatewayException Translate(string action, RefundCapturedPaymentError error)
    {
        if (error.TryGetError(out var err)) return Rejection(action, err.Name, err.Message, err.Details);
        if (error.TryGetNoContent(out var raw)) return RawFailure(action, raw);
        if (error.TryGetRawError(out var fallback)) return RawFailure(action, fallback);
        return new PaymentGatewayException($"PayPal could not {action}.");
    }

    private PaymentGatewayException TranslateVault(string action, CreatePaymentTokenError error)
    {
        if (error.TryGetError1(out var err)) return Rejection(action, err.Name, err.Message, err.Details?.Select(d => new ErrorDetails { Issue = d.Issue, Field = d.Field, Value = d.Value, Description = d.Description }));
        if (error.TryGetRawError(out var raw)) return RawFailure(action, raw);
        return new PaymentGatewayException($"PayPal could not {action}.");
    }

    private PaymentGatewayException TranslateVault(string action, DeletePaymentTokenError error)
    {
        if (error.TryGetError1(out var err)) return Rejection(action, err.Name, err.Message, err.Details?.Select(d => new ErrorDetails { Issue = d.Issue, Field = d.Field, Value = d.Value, Description = d.Description }));
        if (error.TryGetRawError(out var raw)) return RawFailure(action, raw);
        return new PaymentGatewayException($"PayPal could not {action}.");
    }
}
