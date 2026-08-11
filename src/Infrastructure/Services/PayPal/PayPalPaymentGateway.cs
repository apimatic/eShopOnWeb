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

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// PayPal implementation of <see cref="IPaymentGateway"/> over the AsadAli.Checkout.Sdk
/// (<c>PayPalServerSdk</c>). The domain never sees an SDK type: every SDK exception is translated
/// into an <see cref="ApplicationCore.Exceptions.PaymentGatewayException"/> (or a subclass) at this
/// boundary, and card numbers / CVCs are never logged.
/// </summary>
public sealed class PayPalPaymentGateway : IPaymentGateway
{
    private readonly PayPalServerSdkClient _client;

    public PayPalPaymentGateway(PayPalServerSdkClient client) => _client = client;

    public Task<GatewayAuthorization> AuthorizeWithCardAsync(decimal amount, string currencyCode, CardDetails card,
        string idempotencyKey, CancellationToken cancellationToken = default) =>
        CreateAndAuthorizeAsync(amount, currencyCode,
            new PaymentSource { Card = BuildCardRequest(card) }, idempotencyKey, cancellationToken);

    public Task<GatewayAuthorization> AuthorizeWithVaultedCardAsync(decimal amount, string currencyCode, string vaultId,
        string idempotencyKey, CancellationToken cancellationToken = default) =>
        CreateAndAuthorizeAsync(amount, currencyCode,
            new PaymentSource { Card = new CardRequest { VaultId = vaultId } }, idempotencyKey, cancellationToken);

    private async Task<GatewayAuthorization> CreateAndAuthorizeAsync(decimal amount, string currencyCode,
        PaymentSource paymentSource, string idempotencyKey, CancellationToken ct)
    {
        var orderRequest = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PaymentSource = paymentSource,
            PurchaseUnits = new[]
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown { CurrencyCode = currencyCode, Value = FormatAmount(amount) }
                }
            }
        };

        Order order;
        try
        {
            order = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey + "-create",
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: orderRequest,
                prefer: "return=representation",
                ct: ct);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            if (ex.Error.TryGetError(out var err))
                throw new PaymentGatewayException(BuildMessage(err.Name, err.Message, err.Details), ex);
            throw FromRaw("order creation", ex);
        }
        catch (JsonException ex) { throw Malformed(ex); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable(ex); }

        if (RequiresChallenge(order.Status, order.Links))
            throw new PaymentChallengeRequiredException(
                "PayPal requires browser approval (3-D Secure / redirect) for this card; this integration is browser-less and cannot proceed.");

        if (string.IsNullOrEmpty(order.Id))
            throw new PaymentGatewayException("PayPal returned an order without an id; cannot authorize.");

        // When a card is supplied inline with intent=AUTHORIZE, PayPal places the hold as part of
        // order creation, so the authorization is already on the create response. Use it directly;
        // calling AuthorizeOrder again would fail with ORDER_ALREADY_AUTHORIZED.
        var created = ExtractAuthorization(order.PurchaseUnits);
        if (created?.Id is not null)
            return new GatewayAuthorization(order.Id!, created.Id,
                created.Status?.Value ?? "UNKNOWN", ParseDate(created.ExpirationTime));

        OrderAuthorizeResponse authorized;
        try
        {
            authorized = await _client.Orders.AuthorizeOrder(
                id: order.Id!,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey + "-auth",
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: null,
                ct: ct);
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            if (ex.Error.TryGetError(out var err))
                throw new PaymentGatewayException(BuildMessage(err.Name, err.Message, err.Details), ex);
            throw FromRaw("authorization", ex);
        }
        catch (JsonException ex) { throw Malformed(ex); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable(ex); }

        if (RequiresChallenge(authorized.Status, authorized.Links))
            throw new PaymentChallengeRequiredException(
                "PayPal requires browser approval to complete the authorization; this integration is browser-less and cannot proceed.");

        var authorization = ExtractAuthorization(authorized.PurchaseUnits);
        if (authorization?.Id is null)
            throw new PaymentGatewayException("PayPal did not return an authorization for the order.");

        return new GatewayAuthorization(
            order.Id!,
            authorization.Id,
            authorization.Status?.Value ?? "UNKNOWN",
            ParseDate(authorization.ExpirationTime));
    }

    private static AuthorizationWithAdditionalData? ExtractAuthorization(IReadOnlyList<PurchaseUnit>? purchaseUnits) =>
        purchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();

    public async Task<GatewayCapture> CaptureAuthorizationAsync(string authorizationId, decimal amount,
        string currencyCode, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new CaptureRequest
        {
            FinalCapture = true,
            Amount = new Money { CurrencyCode = currencyCode, Value = FormatAmount(amount) }
        };

        CapturedPayment captured;
        try
        {
            captured = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation", // needed for the seller_receivable_breakdown (fee/net)
                ct: cancellationToken);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var err))
            {
                var message = BuildMessage(err.Name, err.Message, err.Details);
                if (IndicatesExpiry(err.Name, err.Message, err.Details))
                    throw new AuthorizationExpiredException(message, ex);
                throw new PaymentGatewayException(message, ex);
            }
            if (ex.Error.TryGetNoContent(out var noContent))
                throw new PaymentGatewayException($"PayPal capture failed (HTTP {(int)noContent.StatusCode}).", ex, retryable: true);
            throw FromRaw("capture", ex);
        }
        catch (JsonException ex) { throw Malformed(ex); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable(ex); }

        if (string.IsNullOrEmpty(captured.Id))
            throw new PaymentGatewayException("PayPal capture response had no capture id.");

        var breakdown = captured.SellerReceivableBreakdown;
        var gross = ParseAmount(breakdown?.GrossAmount?.Value)
                    ?? ParseAmount(captured.Amount?.Value)
                    ?? amount;
        var fee = ParseAmount(breakdown?.PaypalFee?.Value);
        var net = ParseAmount(breakdown?.NetAmount?.Value);
        var currency = captured.Amount?.CurrencyCode ?? breakdown?.GrossAmount?.CurrencyCode ?? currencyCode;

        return new GatewayCapture(captured.Id!, captured.Status?.Value ?? "UNKNOWN", gross, fee, net, currency);
    }

    public async Task<GatewayAuthorization> ReauthorizeAsync(string authorizationId, decimal amount,
        string currencyCode, CancellationToken cancellationToken = default)
    {
        PaymentAuthorization result;
        try
        {
            result = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: null,
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest { Amount = new Money { CurrencyCode = currencyCode, Value = FormatAmount(amount) } },
                ct: cancellationToken);
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            if (ex.Error.TryGetError(out var err))
                throw new PaymentGatewayException(BuildMessage(err.Name, err.Message, err.Details), ex);
            if (ex.Error.TryGetNoContent(out var noContent))
                throw new PaymentGatewayException($"PayPal reauthorization failed (HTTP {(int)noContent.StatusCode}).", ex, retryable: true);
            throw FromRaw("reauthorization", ex);
        }
        catch (JsonException ex) { throw Malformed(ex); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable(ex); }

        if (string.IsNullOrEmpty(result.Id))
            throw new PaymentGatewayException("PayPal reauthorization returned no authorization id.");

        return new GatewayAuthorization(
            string.Empty,
            result.Id!,
            result.Status?.Value ?? "UNKNOWN",
            ParseDate(result.ExpirationTime));
    }

    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: null,
                ct: cancellationToken);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var err))
                throw new PaymentGatewayException(BuildMessage(err.Name, err.Message, err.Details), ex);
            if (ex.Error.TryGetNoContent(out var noContent))
                throw new PaymentGatewayException($"PayPal void failed (HTTP {(int)noContent.StatusCode}).", ex, retryable: true);
            throw FromRaw("void", ex);
        }
        // A successful void returns 204 No Content; the SDK's typed deserialization of an empty
        // body surfaces as a JsonException. That is success, not a failure — the hold is released.
        catch (JsonException) { }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable(ex); }
    }

    public async Task<GatewayRefund> RefundCaptureAsync(string captureId, decimal? amount, string currencyCode,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = amount is null
            ? null
            : new RefundRequest { Amount = new Money { CurrencyCode = currencyCode, Value = FormatAmount(amount.Value) } };

        Refund refund;
        try
        {
            refund = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                ct: cancellationToken);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var err))
                throw new PaymentGatewayException(BuildMessage(err.Name, err.Message, err.Details), ex);
            if (ex.Error.TryGetNoContent(out var noContent))
                throw new PaymentGatewayException($"PayPal refund failed (HTTP {(int)noContent.StatusCode}).", ex, retryable: true);
            throw FromRaw("refund", ex);
        }
        catch (JsonException ex) { throw Malformed(ex); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable(ex); }

        if (string.IsNullOrEmpty(refund.Id))
            throw new PaymentGatewayException("PayPal refund response had no refund id.");

        var refundedAmount = ParseAmount(refund.Amount?.Value) ?? amount ?? 0m;
        var currency = refund.Amount?.CurrencyCode ?? currencyCode;
        return new GatewayRefund(refund.Id!, refund.Status?.Value ?? "UNKNOWN", refundedAmount, currency);
    }

    public async Task<GatewayVaultedCard> VaultCardAsync(CardDetails card, CancellationToken cancellationToken = default)
    {
        var request = new PaymentTokenRequest
        {
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Number = card.Number,
                    Expiry = FormatExpiry(card),
                    SecurityCode = card.SecurityCode,
                    Name = card.CardholderName,
                    BillingAddress = BuildAddress(card)
                }
            }
        };

        PaymentTokenResponse response;
        try
        {
            response = await _client.Vault.CreatePaymentToken(
                payPalRequestId: null,
                body: request,
                ct: cancellationToken);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError1(out var err))
                throw new PaymentGatewayException(BuildMessage(err.Name, err.Message, err.Details), ex);
            throw FromRaw("card vaulting", ex);
        }
        catch (JsonException ex) { throw Malformed(ex); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable(ex); }

        if (HasApprovalLink(response.Links))
            throw new PaymentChallengeRequiredException(
                "PayPal requires browser approval to vault this card; this integration is browser-less and cannot proceed.");

        if (string.IsNullOrEmpty(response.Id))
            throw new PaymentGatewayException("PayPal vaulting returned no payment-token id.");

        var vaultedCard = response.PaymentSource?.Card;
        var last4 = string.IsNullOrEmpty(vaultedCard?.LastDigits) ? Last4Of(card.Number) : vaultedCard!.LastDigits!;
        return new GatewayVaultedCard(
            response.Id!,
            vaultedCard?.Brand?.Value,
            last4,
            vaultedCard?.Expiry,
            card.CardholderName);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(id: vaultId, ct: cancellationToken);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError1(out var err))
                throw new PaymentGatewayException(BuildMessage(err.Name, err.Message, err.Details), ex);
            throw FromRaw("vault deletion", ex);
        }
        catch (JsonException ex) { throw Malformed(ex); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable(ex); }
    }

    public async Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<GatewayTransaction>();
        var startDate = from.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
        var endDate = to.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

        var page = 1;
        int totalPages;
        do
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
                    pageSize: 100,
                    page: page,
                    ct: cancellationToken);
            }
            catch (SdkException<RawError> ex) // SearchTransactions is Case B — RawError directly
            {
                throw new PaymentGatewayException(
                    $"PayPal transaction search failed (HTTP {(int)ex.Error.StatusCode}): {Safe(ex.Error)}", ex);
            }
            catch (JsonException ex) { throw Malformed(ex); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable(ex); }

            totalPages = response.TotalPages ?? 1;

            if (response.TransactionDetails is not null)
            {
                foreach (var detail in response.TransactionDetails)
                {
                    var info = detail.TransactionInfo;
                    if (info?.TransactionId is null)
                        continue;

                    results.Add(new GatewayTransaction(
                        info.TransactionId,
                        info.TransactionStatus ?? "UNKNOWN",
                        ParseAmount(info.TransactionAmount?.Value) ?? 0m,
                        info.TransactionAmount?.CurrencyCode ?? string.Empty,
                        ParseDate(info.TransactionInitiationDate) ?? default,
                        info.TransactionEventCode));
                }
            }

            page++;
        }
        while (page <= totalPages);

        return results;
    }

    // ---- request builders -------------------------------------------------

    private static CardRequest BuildCardRequest(CardDetails card) => new()
    {
        Number = card.Number,
        Expiry = FormatExpiry(card),
        SecurityCode = card.SecurityCode,
        Name = card.CardholderName,
        BillingAddress = BuildAddress(card)
    };

    private static Address BuildAddress(CardDetails card) => new()
    {
        CountryCode = string.IsNullOrWhiteSpace(card.BillingCountryCode) ? "US" : card.BillingCountryCode!,
        AddressLine1 = card.BillingLine1,
        AdminArea2 = card.BillingCity,
        AdminArea1 = card.BillingState,
        PostalCode = card.BillingPostalCode
    };

    private static string FormatAmount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string FormatExpiry(CardDetails card) => $"{card.ExpiryYear}-{card.ExpiryMonth.PadLeft(2, '0')}";

    // ---- response / error helpers ----------------------------------------

    private static bool RequiresChallenge(OrderStatus? status, IReadOnlyList<LinkDescription>? links) =>
        status == OrderStatus.PayerActionRequired || HasApprovalLink(links);

    private static bool HasApprovalLink(IReadOnlyList<LinkDescription>? links)
    {
        if (links is null)
            return false;

        foreach (var link in links)
        {
            var rel = (link.Rel ?? string.Empty).ToUpperInvariant();
            if (rel.Contains("PAYER-ACTION") || rel.Contains("PAYER_ACTION") || rel == "APPROVE")
                return true;
        }
        return false;
    }

    private static bool IndicatesExpiry(string name, string message, IReadOnlyList<ErrorDetails>? details)
    {
        var haystack = (name + " " + message + " " +
            string.Join(" ", (details ?? Array.Empty<ErrorDetails>())
                .Select(d => d.Issue + " " + (d.Description ?? string.Empty))))
            .ToUpperInvariant();

        if (haystack.Contains("EXPIR"))
            return true;
        if (haystack.Contains("AUTHORIZATION") && haystack.Contains("VOID"))
            return true;
        return false;
    }

    private static string BuildMessage(string name, string message, IReadOnlyList<ErrorDetails>? details)
    {
        if (details is { Count: > 0 })
        {
            var joined = string.Join("; ", details.Select(d =>
                string.IsNullOrWhiteSpace(d.Description) ? d.Issue : $"{d.Issue} ({d.Description})"));
            return $"{name}: {joined}";
        }
        return string.IsNullOrWhiteSpace(message) ? name : $"{name}: {message}";
    }

    private static string BuildMessage(string name, string message, IReadOnlyList<ErrorDetails1>? details)
    {
        if (details is { Count: > 0 })
        {
            var joined = string.Join("; ", details.Select(d =>
                string.IsNullOrWhiteSpace(d.Description) ? d.Issue : $"{d.Issue} ({d.Description})"));
            return $"{name}: {joined}";
        }
        return string.IsNullOrWhiteSpace(message) ? name : $"{name}: {message}";
    }

    // A typed error whose body did not match any typed accessor: carry the real HTTP status so a
    // provider 4xx stays a client error rather than being flattened into a fake outage.
    private static PaymentGatewayException FromRaw<TError>(string operation, SdkException<TError> ex)
        where TError : ApiError
    {
        if (ex.Error.TryGetRawError(out var raw))
            return new PaymentGatewayException(
                $"PayPal {operation} failed (HTTP {(int)raw.StatusCode}): {Safe(raw)}", ex,
                retryable: (int)raw.StatusCode >= 500);
        return new PaymentGatewayException($"PayPal {operation} failed.", ex);
    }

    private static PaymentGatewayException Malformed(JsonException ex) =>
        // Applies to a broken 2xx body AND to a non-2xx whose error shape failed to parse: both are
        // deterministic (non-retryable), never a fake outage.
        new("PayPal returned a response that could not be processed.", ex, retryable: false);

    private static PaymentGatewayException Unreachable(Exception ex) =>
        new("PayPal is currently unreachable or timed out.", ex, retryable: true);

    private static string Safe(RawError raw)
    {
        try { return raw.ReadAsString(); }
        catch { return "<unreadable>"; }
    }

    private static decimal? ParseAmount(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d) ? d : null;

    private static string Last4Of(string pan)
    {
        var digits = new string(pan.Where(char.IsDigit).ToArray());
        return digits.Length >= 4 ? digits[^4..] : digits;
    }
}
