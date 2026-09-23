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
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.Extensions.Logging;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// The concrete PayPal gateway over the PayPal Server SDK. One method per SDK operation in scope; every
/// method translates provider failures into <see cref="PayPalGatewayException"/> (or, for a browser
/// approval challenge, <see cref="PaymentChallengeRequiredException"/>), and never lets a raw SDK
/// exception or card data escape.
/// </summary>
public class PayPalGateway : IPayPalGateway
{
    private readonly PayPalServerSdkClient _client;
    private readonly ILogger<PayPalGateway> _logger;

    public PayPalGateway(PayPalServerSdkClient client, ILogger<PayPalGateway> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<PayPalOrderResult> CreateOrderAsync(CreatePayPalOrderRequest request, string requestId,
        CancellationToken ct)
    {
        var body = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new()
                {
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = request.CurrencyCode,
                        Value = MoneyFormatter.Format(request.Amount, request.CurrencyCode)
                    },
                    InvoiceId = request.InvoiceId,
                    CustomId = request.CustomId,
                    Description = Trim(request.Description, 127)
                }
            },
            PaymentSource = BuildPaymentSource(request.PaymentSource)
        };

        var order = await InvokeAsync<CreateOrderError, Order>(
            "create order",
            () => _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: requestId,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: ct),
            e => e.TryGetError(out var err) ? err : null,
            e => e.TryGetRawError(out var raw) ? raw : null,
            ct);

        return MapOrder(order.Id, order.Status, order.PurchaseUnits, order.Links);
    }

    public async Task<AuthorizationResult> AuthorizeOrderAsync(string payPalOrderId, string requestId,
        CancellationToken ct)
    {
        var response = await InvokeAsync<AuthorizeOrderError, OrderAuthorizeResponse>(
            "authorize order",
            () => _client.Orders.AuthorizeOrder(
                id: payPalOrderId,
                payPalMockResponse: null,
                payPalRequestId: requestId,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: null,
                prefer: "return=representation",
                ct: ct),
            e => e.TryGetError(out var err) ? err : null,
            e => e.TryGetRawError(out var raw) ? raw : null,
            ct);

        ThrowIfChallenge("authorize order", response.Status, response.Links);

        var auth = FindAuthorization(response.PurchaseUnits);
        if (auth?.Id is null)
            throw new PayPalGatewayException(
                "PayPal did not return an authorization for the order. The card may have been declined.");

        return new AuthorizationResult(auth.Id, auth.Status?.Value, ParseDate(auth.ExpirationTime),
            ParseDate(auth.CreateTime));
    }

    public async Task<PayPalOrderResult> GetOrderAsync(string payPalOrderId, CancellationToken ct)
    {
        var order = await InvokeAsync<GetOrderError, Order>(
            "get order",
            () => _client.Orders.GetOrder(
                id: payPalOrderId,
                fields: null,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                ct: ct),
            e => e.TryGetError(out var err) ? err : null,
            e => e.TryGetRawError(out var raw) ? raw : null,
            ct);

        return MapOrder(order.Id, order.Status, order.PurchaseUnits, order.Links);
    }

    public async Task<CaptureResult> CaptureAuthorizationAsync(string authorizationId, string requestId,
        CancellationToken ct)
    {
        var capture = await InvokeAsync<CaptureAuthorizedPaymentError, CapturedPayment>(
            "capture payment",
            () => _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: requestId,
                payPalAuthAssertion: null,
                body: new CaptureRequest { FinalCapture = true },
                prefer: "return=representation",
                ct: ct),
            e => e.TryGetError(out var err) ? err : null,
            e => e.TryGetNoContent(out var nc) ? nc : (e.TryGetRawError(out var raw) ? raw : null),
            ct);

        var breakdown = capture.SellerReceivableBreakdown;
        return new CaptureResult(
            capture.Id ?? string.Empty,
            capture.Status?.Value,
            MoneyFormatter.Parse(breakdown?.GrossAmount?.Value) ?? MoneyFormatter.Parse(capture.Amount?.Value),
            MoneyFormatter.Parse(breakdown?.PaypalFee?.Value),
            MoneyFormatter.Parse(breakdown?.NetAmount?.Value),
            breakdown?.GrossAmount?.CurrencyCode ?? capture.Amount?.CurrencyCode,
            ParseDate(capture.CreateTime));
    }

    public async Task<AuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken ct)
    {
        var auth = await InvokeAsync<GetAuthorizedPaymentError, PaymentAuthorization>(
            "get authorization",
            () => _client.Payments.GetAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                ct: ct),
            e => e.TryGetError(out var err) ? err : null,
            e => e.TryGetNoContent(out var nc) ? nc : (e.TryGetRawError(out var raw) ? raw : null),
            ct);

        return new AuthorizationResult(auth.Id ?? authorizationId, auth.Status?.Value,
            ParseDate(auth.ExpirationTime), ParseDate(auth.CreateTime));
    }

    public async Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, string requestId,
        CancellationToken ct)
    {
        var auth = await InvokeAsync<ReauthorizePaymentError, PaymentAuthorization>(
            "reauthorize payment",
            () => _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: requestId,
                payPalAuthAssertion: null,
                body: null,
                prefer: "return=representation",
                ct: ct),
            e => e.TryGetError(out var err) ? err : null,
            e => e.TryGetNoContent(out var nc) ? nc : (e.TryGetRawError(out var raw) ? raw : null),
            ct);

        return new AuthorizationResult(auth.Id ?? authorizationId, auth.Status?.Value,
            ParseDate(auth.ExpirationTime), ParseDate(auth.CreateTime));
    }

    public async Task<VoidResult> VoidAuthorizationAsync(string authorizationId, CancellationToken ct)
    {
        var auth = await InvokeAsync<VoidPaymentError, PaymentAuthorization>(
            "void authorization",
            () => _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: null,
                prefer: "return=representation",
                ct: ct),
            e => e.TryGetError(out var err) ? err : null,
            e => e.TryGetNoContent(out var nc) ? nc : (e.TryGetRawError(out var raw) ? raw : null),
            ct);

        return new VoidResult(auth.Status?.Value);
    }

    public async Task<RefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currencyCode,
        string idempotencyKey, string? invoiceId, CancellationToken ct)
    {
        var body = new RefundRequest
        {
            Amount = amount is null
                ? null
                : new Money { CurrencyCode = currencyCode, Value = MoneyFormatter.Format(amount.Value, currencyCode) },
            CustomId = Trim(idempotencyKey, 127),
            InvoiceId = Trim(invoiceId, 127)
        };

        var refund = await InvokeAsync<RefundCapturedPaymentError, Refund>(
            "refund payment",
            () => _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: ct),
            e => e.TryGetError(out var err) ? err : null,
            e => e.TryGetNoContent(out var nc) ? nc : (e.TryGetRawError(out var raw) ? raw : null),
            ct);

        return new RefundResult(refund.Id ?? string.Empty, refund.Status?.Value,
            MoneyFormatter.Parse(refund.Amount?.Value), refund.Amount?.CurrencyCode);
    }

    public async Task<VaultCardResult> CreateVaultCardAsync(VaultCardRequest request, string requestId,
        CancellationToken ct)
    {
        var body = new PaymentTokenRequest
        {
            Customer = new Customer { MerchantCustomerId = Trim(request.MerchantCustomerId, 64) },
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Number = request.Card.Number,
                    Expiry = request.Card.Expiry,
                    SecurityCode = request.Card.SecurityCode,
                    Name = request.Card.CardholderName,
                    BillingAddress = BuildAddress(request.Card.BillingAddress)
                }
            }
        };

        var token = await InvokeAsync<CreatePaymentTokenError, PaymentTokenResponse>(
            "vault card",
            () => _client.Vault.CreatePaymentToken(payPalRequestId: requestId, body: body, ct: ct),
            e => e.TryGetError(out var err) ? err : null,
            e => e.TryGetRawError(out var raw) ? raw : null,
            ct);

        if (string.IsNullOrEmpty(token.Id))
            throw new PayPalGatewayException("PayPal did not return a vault id for the saved card.");

        var card = token.PaymentSource?.Card;
        return new VaultCardResult(token.Id!, card?.Brand?.Value, card?.LastDigits, card?.Expiry, card?.Name);
    }

    public async Task DeleteVaultCardAsync(string vaultId, CancellationToken ct)
    {
        await InvokeAsync<DeletePaymentTokenError, bool>(
            "delete vaulted card",
            async () =>
            {
                await _client.Vault.DeletePaymentToken(id: vaultId, ct: ct);
                return true;
            },
            e => e.TryGetError(out var err) ? err : null,
            e => e.TryGetRawError(out var raw) ? raw : null,
            ct);
    }

    public async Task<TransactionSearchPage> SearchTransactionsAsync(string startDate, string endDate, int page,
        int pageSize, CancellationToken ct)
    {
        SearchResponse response;
        try
        {
            response = await _client.TransactionSearch.SearchTransactions(
                startDate: startDate,
                endDate: endDate,
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
                pageSize: pageSize,
                page: page,
                ct: ct);
        }
        catch (SdkException<RawError> ex)
        {
            throw new PayPalGatewayException(
                $"PayPal error during transaction search: HTTP {(int)ex.Error.StatusCode}",
                providerStatusCode: (int)ex.Error.StatusCode, innerException: ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalGatewayException(
                "PayPal returned a transaction-search response that could not be processed.", innerException: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new PayPalGatewayException("PayPal was unreachable during transaction search.",
                innerException: ex) { OutcomeUnknown = true };
        }

        var transactions = (response.TransactionDetails ?? new List<TransactionDetails>())
            .Select(d => d.TransactionInfo)
            .Where(i => i is not null)
            .Select(i => new PayPalTransaction(
                i!.TransactionId,
                i.InvoiceId,
                MoneyFormatter.Parse(i.TransactionAmount?.Value),
                MoneyFormatter.Parse(i.FeeAmount?.Value),
                i.TransactionAmount?.CurrencyCode,
                i.TransactionStatus,
                ParseDate(i.TransactionInitiationDate)))
            .ToList();

        return new TransactionSearchPage(transactions, response.Page ?? page, response.TotalPages ?? 1);
    }

    // ----- helpers -----

    private static PaymentSource? BuildPaymentSource(PaymentSourceInput source)
    {
        if (!string.IsNullOrWhiteSpace(source.VaultId))
        {
            return new PaymentSource { Card = new CardRequest { VaultId = source.VaultId } };
        }

        if (source.Card is not null)
        {
            var c = source.Card;
            return new PaymentSource
            {
                Card = new CardRequest
                {
                    Number = c.Number,
                    Expiry = c.Expiry,
                    SecurityCode = c.SecurityCode,
                    Name = c.CardholderName,
                    BillingAddress = BuildAddress(c.BillingAddress)
                }
            };
        }

        return null;
    }

    private static Address? BuildAddress(CardBillingAddress? input)
    {
        if (input is null || string.IsNullOrWhiteSpace(input.CountryCode))
            return null;

        return new Address
        {
            AddressLine1 = input.AddressLine1,
            AddressLine2 = input.AddressLine2,
            AdminArea1 = input.AdminArea1,
            AdminArea2 = input.AdminArea2,
            PostalCode = input.PostalCode,
            CountryCode = input.CountryCode!
        };
    }

    private PayPalOrderResult MapOrder(string? id, OrderStatus? status,
        IReadOnlyList<PurchaseUnit>? purchaseUnits, IReadOnlyList<LinkDescription>? links)
    {
        var requiresAction = IsChallenge(status, links);
        var auth = FindAuthorization(purchaseUnits);
        var capture = FindCapture(purchaseUnits);
        return new PayPalOrderResult(id ?? string.Empty, status?.Value, requiresAction, auth?.Id,
            auth?.Status?.Value, capture?.Id, capture?.Status?.Value);
    }

    private static OrdersCapture? FindCapture(IReadOnlyList<PurchaseUnit>? purchaseUnits)
    {
        if (purchaseUnits is null) return null;
        foreach (var pu in purchaseUnits)
        {
            var capture = pu.Payments?.Captures?.FirstOrDefault();
            if (capture is not null) return capture;
        }
        return null;
    }

    private static AuthorizationWithAdditionalData? FindAuthorization(IReadOnlyList<PurchaseUnit>? purchaseUnits)
    {
        if (purchaseUnits is null) return null;
        foreach (var pu in purchaseUnits)
        {
            var auth = pu.Payments?.Authorizations?.FirstOrDefault();
            if (auth is not null) return auth;
        }
        return null;
    }

    private static bool IsChallenge(OrderStatus? status, IReadOnlyList<LinkDescription>? links)
    {
        if (status is not null && status.Value == OrderStatus.PayerActionRequired.Value)
            return true;
        return links?.Any(l =>
            string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(l.Rel, "approve", StringComparison.OrdinalIgnoreCase)) ?? false;
    }

    private void ThrowIfChallenge(string op, OrderStatus? status, IReadOnlyList<LinkDescription>? links)
    {
        if (IsChallenge(status, links))
        {
            throw new PaymentChallengeRequiredException(
                $"PayPal requires the shopper to approve this payment in a browser (challenge) during {op}. " +
                "This integration does not support a browser approval round-trip.");
        }
    }

    private static string? Trim(string? value, int max) =>
        string.IsNullOrEmpty(value) ? value : (value.Length <= max ? value : value.Substring(0, max));

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d)
            ? d
            : null;

    // Case-A invoker for Orders/Vault operations (typed Error + TryGetRawError only).
    private async Task<T> InvokeAsync<TError, T>(
        string op,
        Func<Task<T>> call,
        Func<TError, Error?> getTypedError,
        Func<TError, RawError?> getRawError,
        CancellationToken ct)
        where TError : ApiError
    {
        try
        {
            return await call();
        }
        catch (SdkException<TError> ex)
        {
            throw MapTyped(op, ex, getTypedError, getRawError);
        }
        catch (JsonException ex)
        {
            throw new PayPalGatewayException(
                $"PayPal returned a response for {op} that could not be processed.", innerException: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new PayPalGatewayException($"PayPal was unreachable during {op}.", innerException: ex)
            {
                OutcomeUnknown = true
            };
        }
    }

    private PayPalGatewayException MapTyped<TError>(string op, SdkException<TError> ex,
        Func<TError, Error?> getTypedError, Func<TError, RawError?> getRawError)
        where TError : ApiError
    {
        var typed = getTypedError(ex.Error);
        if (typed is not null)
        {
            var issue = typed.Details is { Count: > 0 } ? typed.Details[0].Issue : null;
            var message = $"PayPal rejected the {op} request: {typed.Name} - {typed.Message}"
                + (issue is not null ? $" ({issue})" : string.Empty);
            _logger.LogWarning("PayPal {Operation} rejected: {Name} {Issue} debug_id={DebugId}",
                op, typed.Name, issue, typed.DebugId);
            return new PayPalGatewayException(message, debugId: typed.DebugId, innerException: ex)
            {
                ProviderName = typed.Name,
                ProviderIssue = issue
            };
        }

        var raw = getRawError(ex.Error);
        if (raw is not null)
        {
            _logger.LogWarning("PayPal {Operation} error: HTTP {Status}", op, (int)raw.StatusCode);
            return new PayPalGatewayException($"PayPal error during {op}: HTTP {(int)raw.StatusCode}",
                providerStatusCode: (int)raw.StatusCode, innerException: ex);
        }

        return new PayPalGatewayException($"PayPal returned an unrecognised error during {op}.",
            innerException: ex);
    }
}
