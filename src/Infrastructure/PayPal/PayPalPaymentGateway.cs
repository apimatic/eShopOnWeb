using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PaymentGateway;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using AppBillingAddress = Microsoft.eShopWeb.ApplicationCore.Interfaces.PaymentGateway.BillingAddress;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// PayPal implementation of <see cref="IPaymentGateway"/>. Confines the PayPal SDK to Infrastructure
/// and translates every provider failure into a <see cref="PaymentGatewayException"/> (or subtype)
/// carrying a caller-safe message and an appropriate HTTP status — never the SDK's raw detail.
/// </summary>
public class PayPalPaymentGateway : IPaymentGateway
{
    private readonly PayPalServerSdkClient _client;

    public PayPalPaymentGateway(PayPalServerSdkClient client)
    {
        _client = client;
    }

    public async Task<AuthorizationResult> AuthorizeAsync(AuthorizeRequest request, CancellationToken cancellationToken = default)
    {
        var card = request.VaultId is not null
            ? new CardRequest { VaultId = request.VaultId }
            : new CardRequest
            {
                Number = request.Card!.Number,
                Expiry = request.Card.Expiry,
                SecurityCode = request.Card.SecurityCode,
                Name = request.Card.CardholderName,
                BillingAddress = ToSdkAddress(request.Card.BillingAddress)
            };

        var orderRequest = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new()
                {
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = request.CurrencyCode,
                        Value = FormatAmount(request.Amount)
                    },
                    InvoiceId = request.MerchantReference,
                    CustomId = request.BuyerReference
                }
            }
            // The card is supplied on AuthorizeOrder (below), not on create: creating an
            // AUTHORIZE order with the payment source auto-authorizes it, which would make the
            // subsequent AuthorizeOrder call fail with ORDER_ALREADY_AUTHORIZED.
        };

        Order created;
        try
        {
            created = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: request.CreateRequestId,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: orderRequest,
                prefer: "return=representation",
                ct: cancellationToken);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            if (ex.Error.TryGetError(out var errorBody))
                throw Typed(errorBody);
            if (ex.Error.TryGetRawError(out var raw))
                throw FromRaw(raw);
            throw new PaymentGatewayException("PayPal rejected the request.", 502);
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw Unreachable(ex);
        }

        StopIfChallenge(created.Status);

        OrderAuthorizeResponse authorized;
        try
        {
            authorized = await _client.Orders.AuthorizeOrder(
                id: created.Id!,
                payPalMockResponse: null,
                payPalRequestId: request.AuthorizeRequestId,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: new OrderAuthorizeRequest
                {
                    PaymentSource = new OrderAuthorizeRequestPaymentSource { Card = card }
                },
                prefer: "return=representation",
                ct: cancellationToken);
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            if (ex.Error.TryGetError(out var errorBody))
                throw Typed(errorBody);
            if (ex.Error.TryGetRawError(out var raw))
                throw FromRaw(raw);
            throw new PaymentGatewayException("PayPal rejected the request.", 502);
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw Unreachable(ex);
        }

        StopIfChallenge(authorized.Status);

        var authorization = authorized.PurchaseUnits?
            .SelectMany(pu => pu.Payments?.Authorizations ?? Enumerable.Empty<AuthorizationWithAdditionalData>())
            .FirstOrDefault();

        if (authorization?.Id is null)
            throw new PaymentGatewayException("PayPal did not return an authorization for the order.", 502);

        return new AuthorizationResult(
            created.Id!,
            authorization.Id,
            authorization.Status?.Value,
            ParseDate(authorization.ExpirationTime));
    }

    public async Task<CaptureResult> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        CapturedPayment capture;
        try
        {
            capture = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: null, // full capture of the whole hold
                prefer: "return=representation",
                ct: cancellationToken);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var errorBody))
                throw Typed(errorBody);
            if (ex.Error.TryGetNoContent(out var noContent))
                throw FromRaw(noContent);
            if (ex.Error.TryGetRawError(out var raw))
                throw FromRaw(raw);
            throw new PaymentGatewayException("PayPal rejected the request.", 502);
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw Unreachable(ex);
        }

        var breakdown = capture.SellerReceivableBreakdown;
        var gross = ParseAmount(breakdown?.GrossAmount?.Value) ?? ParseAmount(capture.Amount?.Value) ?? 0m;
        var currency = breakdown?.GrossAmount?.CurrencyCode ?? capture.Amount?.CurrencyCode ?? string.Empty;

        return new CaptureResult(
            capture.Id!,
            capture.Status?.Value,
            gross,
            ParseAmount(breakdown?.PaypalFee?.Value),
            ParseAmount(breakdown?.NetAmount?.Value),
            currency);
    }

    public async Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        PaymentAuthorization reauthorized;
        try
        {
            reauthorized = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest { Amount = Money(amount, currencyCode) },
                prefer: "return=representation",
                ct: cancellationToken);
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            // A stale authorization that can no longer be renewed must be reported in operator terms.
            if (ex.Error.TryGetError(out var errorBody))
                throw NotRenewable(errorBody);
            // A 500/other raw failure is a provider outage, not a "not renewable" condition.
            if (ex.Error.TryGetNoContent(out var noContent))
                throw FromRaw(noContent);
            if (ex.Error.TryGetRawError(out var raw))
                throw FromRaw(raw);
            throw new AuthorizationNotRenewableException(
                "The authorization could not be renewed; place and pay for a new order instead.");
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw Unreachable(ex);
        }

        if (reauthorized.Id is null)
            throw new AuthorizationNotRenewableException(
                "The authorization could not be renewed; place and pay for a new order instead.");

        return new AuthorizationResult(
            string.Empty,
            reauthorized.Id,
            reauthorized.Status?.Value,
            ParseDate(reauthorized.ExpirationTime));
    }

    public async Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: idempotencyKey,
                prefer: "return=minimal",
                ct: cancellationToken);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var errorBody))
                throw Typed(errorBody);
            if (ex.Error.TryGetNoContent(out var noContent))
                throw FromRaw(noContent);
            if (ex.Error.TryGetRawError(out var raw))
                throw FromRaw(raw);
            throw new PaymentGatewayException("PayPal rejected the request.", 502);
        }
        catch (System.Text.Json.JsonException)
        {
            // A successful void returns HTTP 204 No Content, which the SDK cannot deserialize into
            // its declared return type. An empty body here means the void succeeded — a real failure
            // arrives as SdkException<VoidPaymentError> above, not as a JsonException.
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable(ex);
        }
    }

    public async Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currencyCode,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = amount.HasValue ? new RefundRequest { Amount = Money(amount.Value, currencyCode) } : null;

        Refund refund;
        try
        {
            refund = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: cancellationToken);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var errorBody))
                throw Typed(errorBody);
            if (ex.Error.TryGetNoContent(out var noContent))
                throw FromRaw(noContent);
            if (ex.Error.TryGetRawError(out var raw))
                throw FromRaw(raw);
            throw new PaymentGatewayException("PayPal rejected the request.", 502);
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw Unreachable(ex);
        }

        return new RefundResult(
            refund.Id!,
            refund.Status?.Value,
            ParseAmount(refund.Amount?.Value) ?? amount ?? 0m,
            refund.Amount?.CurrencyCode ?? currencyCode);
    }

    public async Task<VaultedCardResult> VaultCardAsync(CardDetails card, string? payPalCustomerId,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new PaymentTokenRequest
        {
            Customer = payPalCustomerId is null ? null : new Customer { Id = payPalCustomerId },
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    Name = card.CardholderName,
                    BillingAddress = ToSdkAddress(card.BillingAddress)
                }
            }
        };

        PaymentTokenResponse token;
        try
        {
            token = await _client.Vault.CreatePaymentToken(
                payPalRequestId: idempotencyKey,
                body: body,
                ct: cancellationToken);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError1(out var errorBody))
                throw Typed1(errorBody);
            if (ex.Error.TryGetRawError(out var raw))
                throw FromRaw(raw);
            throw new PaymentGatewayException("PayPal rejected the request.", 502);
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw Unreachable(ex);
        }

        var cardEntity = token.PaymentSource?.Card;
        return new VaultedCardResult(
            token.Id!,
            token.Customer?.Id ?? payPalCustomerId,
            cardEntity?.Brand?.Value,
            cardEntity?.LastDigits,
            cardEntity?.Expiry);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(id: vaultId, ct: cancellationToken);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError1(out var errorBody))
                throw Typed1(errorBody);
            if (ex.Error.TryGetRawError(out var raw))
                throw FromRaw(raw);
            throw new PaymentGatewayException("PayPal rejected the request.", 502);
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw Unreachable(ex);
        }
    }

    public async Task<IReadOnlyList<ReconciliationTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ReconciliationTransaction>();
        var page = 1;

        while (true)
        {
            SearchResponse response;
            try
            {
                response = await _client.TransactionSearch.SearchTransactions(
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
                    ct: cancellationToken);
            }
            catch (SdkException<RawError> ex)
            {
                throw FromRaw(ex.Error);
            }
            catch (Exception ex) when (IsTransport(ex))
            {
                throw Unreachable(ex);
            }

            if (response.TransactionDetails is { } details)
            {
                foreach (var detail in details)
                {
                    var info = detail.TransactionInfo;
                    if (info?.TransactionId is null)
                        continue;

                    results.Add(new ReconciliationTransaction(
                        info.TransactionId,
                        info.TransactionStatus,
                        ParseAmount(info.TransactionAmount?.Value),
                        info.TransactionAmount?.CurrencyCode,
                        info.InvoiceId,
                        ParseDate(info.TransactionInitiationDate),
                        ParseDate(info.TransactionUpdatedDate)));
                }
            }

            var totalPages = response.TotalPages ?? 0;
            if (page >= totalPages)
                break;
            page++;
        }

        return results;
    }

    // ----- helpers -----

    private static Address? ToSdkAddress(AppBillingAddress? billing)
    {
        if (billing is null)
            return null;

        return new Address
        {
            AddressLine1 = billing.AddressLine1,
            AddressLine2 = billing.AddressLine2,
            AdminArea2 = billing.AdminArea2,
            AdminArea1 = billing.AdminArea1,
            PostalCode = billing.PostalCode,
            CountryCode = billing.CountryCode
        };
    }

    private static Money Money(decimal amount, string currencyCode) =>
        new() { CurrencyCode = currencyCode, Value = FormatAmount(amount) };

    private static string FormatAmount(decimal amount) =>
        amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal? ParseAmount(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;

    private static string FormatSearchDate(DateTimeOffset value)
    {
        var offset = value.Offset;
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var offsetText = $"{sign}{Math.Abs(offset.Hours):D2}{Math.Abs(offset.Minutes):D2}";
        return value.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) + offsetText;
    }

    private static void StopIfChallenge(OrderStatus? status)
    {
        if (status == OrderStatus.PayerActionRequired)
            throw new PaymentChallengeRequiredException(
                "PayPal requires the shopper to approve this card payment in a browser (challenge/3-D Secure). " +
                "This integration is no-browser, so the payment was stopped.");
    }

    private static bool IsTransport(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException;

    private static PaymentGatewayException Unreachable(Exception ex) =>
        ex is System.Text.Json.JsonException
            ? new PaymentGatewayException("PayPal returned a response that could not be processed.", 502, innerException: ex)
            : new PaymentGatewayException("The payment provider is currently unreachable.", 502, innerException: ex);

    // The typed body of an Orders/Payments {Op}Error (read via env.TryGetError(out Error)).
    private static PaymentGatewayException Typed(Error body)
    {
        var details = body.Details is null
            ? null
            : string.Join("; ", body.Details.Select(d => $"{d.Issue} {d.Description} (at {d.Field})".Trim()));
        return new PaymentGatewayException(BuildMessage(body.Name, body.Message, details, body.DebugId), 422, body.DebugId);
    }

    // The typed body of a Vault {Op}Error (read via env.TryGetError1(out Error1)).
    private static PaymentGatewayException Typed1(Error1 body)
    {
        var details = body.Details is null
            ? null
            : string.Join("; ", body.Details.Select(d => $"{d.Issue} {d.Description} (at {d.Field})".Trim()));
        return new PaymentGatewayException(BuildMessage(body.Name, body.Message, details, body.DebugId), 422, body.DebugId);
    }

    private static AuthorizationNotRenewableException NotRenewable(Error body)
    {
        var issue = body.Details?.FirstOrDefault()?.Issue;
        var parts = new[] { body.Name, body.Message, issue }.Where(s => !string.IsNullOrWhiteSpace(s));
        var suffix = parts.Any() ? $" ({string.Join(": ", parts)})" : string.Empty;
        return new AuthorizationNotRenewableException(
            "The authorization is stale and can no longer be renewed; place and pay for a new order instead." + suffix,
            body.DebugId);
    }

    private static PaymentGatewayException FromRaw(RawError raw)
    {
        var status = (int)raw.StatusCode;
        var mapped = status is >= 400 and < 500 ? status : 502;
        return new PaymentGatewayException($"PayPal request failed (HTTP {status}).", mapped);
    }

    private static string BuildMessage(string? name, string? message, string? details = null, string? debugId = null)
    {
        var text = string.Join(": ", new[] { name, message }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var result = string.IsNullOrWhiteSpace(text) ? "PayPal rejected the request." : $"PayPal rejected the request: {text}";
        if (!string.IsNullOrWhiteSpace(details))
            result += $" [{details}]";
        if (!string.IsNullOrWhiteSpace(debugId))
            result += $" (debug_id: {debugId})";
        return result;
    }
}
