using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.Extensions.Logging;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// PayPal implementation of <see cref="IPaymentGateway"/> over the PayPalServerSdk client.
/// Contract facts (signatures, wire names, error accessors) come from paypal-plan.md.
/// Full card details pass through to PayPal only; they are never persisted or logged.
/// </summary>
public class PayPalPaymentGateway : IPaymentGateway
{
    private const string Representation = "return=representation";

    private readonly PayPalServerSdkClient _client;
    private readonly ILogger<PayPalPaymentGateway> _logger;

    public PayPalPaymentGateway(PayPalServerSdkClient client, ILogger<PayPalPaymentGateway> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<PaymentAuthorizationResult> AuthorizeCardAsync(CardDetails card, decimal amount, string currency, string referenceId, string idempotencyKey, CancellationToken ct = default)
    {
        var request = BuildAuthorizeRequest(amount, currency, referenceId, new PaymentSource { Card = BuildCardRequest(card) });

        try
        {
            var order = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: request,
                prefer: Representation,
                requestOptions: null,
                ct: ct);

            return await ReadAuthorizationAsync(order, ct);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw MapCreateOrderError(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unavailable(ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable(ex);
        }
    }

    public async Task<PaymentAuthorizationResult> AuthorizeVaultedCardAsync(string vaultTokenId, decimal amount, string currency, string referenceId, string idempotencyKey, CancellationToken ct = default)
    {
        var request = BuildAuthorizeRequest(amount, currency, referenceId, new PaymentSource { Card = new CardRequest { VaultId = vaultTokenId } });

        try
        {
            var order = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: request,
                prefer: Representation,
                requestOptions: null,
                ct: ct);

            return await ReadAuthorizationAsync(order, ct);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw MapCreateOrderError(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unavailable(ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable(ex);
        }
    }

    public async Task<AuthorizationState> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default)
    {
        try
        {
            var authorization = await _client.Payments.GetAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                requestOptions: null,
                ct: ct);

            return new AuthorizationState(authorization.Id, authorization.Status?.Value ?? string.Empty, ParseTime(authorization.ExpirationTime));
        }
        catch (SdkException<GetAuthorizedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error))
            {
                throw MapTypedError(error.Name, error.Message, error.Details?.FirstOrDefault()?.Issue, ex);
            }
            if (ex.Error.TryGetNoContent(out var noContent))
            {
                throw MapRawError(noContent, ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRawError(raw, ex);
            }
            throw Unknown(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unavailable(ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable(ex);
        }
    }

    public async Task<AuthorizationState> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            var authorization = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest { Amount = new Money { CurrencyCode = currency, Value = Format(amount) } },
                prefer: Representation,
                requestOptions: null,
                ct: ct);

            return new AuthorizationState(authorization.Id, authorization.Status?.Value ?? string.Empty, ParseTime(authorization.ExpirationTime));
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error))
            {
                throw MapTypedError(error.Name, error.Message, error.Details?.FirstOrDefault()?.Issue, ex);
            }
            if (ex.Error.TryGetNoContent(out var noContent))
            {
                throw MapRawError(noContent, ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRawError(raw, ex);
            }
            throw Unknown(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unavailable(ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable(ex);
        }
    }

    public async Task<CaptureResult> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            var capture = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: new CaptureRequest { FinalCapture = true },
                prefer: Representation,
                requestOptions: null,
                ct: ct);

            var breakdown = capture.SellerReceivableBreakdown;
            var gross = ParseMoney(breakdown?.GrossAmount) ?? ParseMoney(capture.Amount) ?? 0m;
            return new CaptureResult(
                capture.Id,
                capture.Status?.Value ?? string.Empty,
                gross,
                ParseMoney(breakdown?.PaypalFee),
                ParseMoney(breakdown?.NetAmount),
                breakdown?.GrossAmount?.CurrencyCode ?? capture.Amount?.CurrencyCode ?? string.Empty);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error))
            {
                throw MapTypedError(error.Name, error.Message, error.Details?.FirstOrDefault()?.Issue, ex);
            }
            if (ex.Error.TryGetNoContent(out var noContent))
            {
                throw MapRawError(noContent, ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRawError(raw, ex);
            }
            throw Unknown(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unavailable(ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable(ex);
        }
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            await _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: idempotencyKey,
                prefer: Representation,
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error))
            {
                throw MapTypedError(error.Name, error.Message, error.Details?.FirstOrDefault()?.Issue, ex);
            }
            if (ex.Error.TryGetNoContent(out var noContent))
            {
                throw MapRawError(noContent, ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRawError(raw, ex);
            }
            throw Unknown(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unavailable(ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable(ex);
        }
    }

    public async Task<RefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            var refund = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: amount.HasValue
                    ? new RefundRequest { Amount = new Money { CurrencyCode = currency, Value = Format(amount.Value) } }
                    : null,
                prefer: Representation,
                requestOptions: null,
                ct: ct);

            return new RefundResult(
                refund.Id,
                refund.Status?.Value ?? string.Empty,
                ParseMoney(refund.Amount) ?? amount ?? 0m,
                refund.Amount?.CurrencyCode ?? currency);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error))
            {
                throw MapTypedError(error.Name, error.Message, error.Details?.FirstOrDefault()?.Issue, ex);
            }
            if (ex.Error.TryGetNoContent(out var noContent))
            {
                throw MapRawError(noContent, ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRawError(raw, ex);
            }
            throw Unknown(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unavailable(ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable(ex);
        }
    }

    public async Task<VaultedCardResult> VaultCardAsync(CardDetails card, string customerId, string idempotencyKey, CancellationToken ct = default)
    {
        var request = new PaymentTokenRequest
        {
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
            },
            Customer = new Customer { MerchantCustomerId = customerId }
        };

        try
        {
            var token = await _client.Vault.CreatePaymentToken(
                payPalRequestId: idempotencyKey,
                body: request,
                requestOptions: null,
                ct: ct);

            var cardEntity = token.PaymentSource?.Card;
            return new VaultedCardResult(
                token.Id,
                cardEntity?.Brand?.Value,
                cardEntity?.LastDigits,
                cardEntity?.Expiry,
                cardEntity?.Name);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError1(out var error))
            {
                throw MapTypedError(error.Name, error.Message, error.Details?.FirstOrDefault()?.Issue, ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRawError(raw, ex);
            }
            throw Unknown(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unavailable(ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable(ex);
        }
    }

    public async Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken ct = default)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(id: vaultTokenId, requestOptions: null, ct: ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError1(out var error))
            {
                throw MapTypedError(error.Name, error.Message, error.Details?.FirstOrDefault()?.Issue, ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRawError(raw, ex);
            }
            throw Unknown(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unavailable(ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable(ex);
        }
    }

    public async Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var transactions = new List<GatewayTransaction>();

        // The reporting API accepts at most a 31-day range per call: chunk longer ranges.
        foreach (var (windowStart, windowEnd) in ChunkRange(from, to))
        {
            var page = 1;
            int? totalPages;
            do
            {
                SearchResponse response;
                try
                {
                    response = await _client.TransactionSearch.SearchTransactions(
                        startDate: FormatRfc3339(windowStart),
                        endDate: FormatRfc3339(windowEnd),
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
                }
                catch (SdkException<RawError> ex)
                {
                    // TransactionSearch is the SDK's raw-error operation: no typed accessors.
                    throw MapRawError(ex.Error, ex);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    throw Unavailable(ex);
                }
                catch (JsonException ex)
                {
                    throw Unprocessable(ex);
                }

                totalPages = response.TotalPages;
                foreach (var detail in response.TransactionDetails ?? Enumerable.Empty<TransactionDetails>())
                {
                    var info = detail.TransactionInfo;
                    if (info is null) continue;

                    transactions.Add(new GatewayTransaction(
                        info.TransactionId,
                        info.PaypalReferenceId,
                        info.PaypalReferenceIdType?.Value,
                        info.TransactionStatus,
                        ParseMoney(info.TransactionAmount),
                        info.TransactionAmount?.CurrencyCode,
                        ParseMoney(info.FeeAmount),
                        ParseTime(info.TransactionInitiationDate)));
                }

                page++;
            }
            while (totalPages.HasValue && page <= totalPages.Value);
        }

        return transactions;
    }

    private static OrderRequest BuildAuthorizeRequest(decimal amount, string currency, string referenceId, PaymentSource paymentSource)
    {
        return new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = currency,
                        Value = Format(amount)
                    },
                    ReferenceId = referenceId,
                    CustomId = referenceId
                }
            },
            PaymentSource = paymentSource
        };
    }

    private async Task<PaymentAuthorizationResult> ReadAuthorizationAsync(Order order, CancellationToken ct)
    {
        // A payer-action (3DS) challenge is a hard stop for this server-to-server integration.
        if (order.Status == OrderStatus.PayerActionRequired)
        {
            _logger.LogWarning("PayPal order {PayPalOrderId} requires payer action (3DS); this integration does not support approval round-trips.", order.Id);
            throw new PaymentGatewayException(PaymentGatewayErrorKind.PayerActionRequired,
                "PayPal requires the shopper to approve this payment in a browser (3D Secure). This integration cannot complete such payments.");
        }

        var authorization = order.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();

        if (authorization is null && order.Id is not null)
        {
            // Defensive: if the create response did not inline the authorization, poll once.
            try
            {
                var fetched = await _client.Orders.GetOrder(
                    id: order.Id,
                    fields: null,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    requestOptions: null,
                    ct: ct);
                authorization = fetched.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
            }
            catch (SdkException<GetOrderError> ex)
            {
                if (ex.Error.TryGetError(out var error))
                {
                    throw MapTypedError(error.Name, error.Message, error.Details?.FirstOrDefault()?.Issue, ex);
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw MapRawError(raw, ex);
                }
                throw Unknown(ex);
            }
        }

        if (authorization is null || authorization.Id is null)
        {
            throw new PaymentGatewayException(PaymentGatewayErrorKind.Unknown,
                $"PayPal did not confirm an authorization for order {order.Id} (status {order.Status}).");
        }

        return new PaymentAuthorizationResult(
            order.Id ?? string.Empty,
            authorization.Id,
            authorization.Status?.Value ?? string.Empty,
            ParseTime(authorization.ExpirationTime));
    }

    private static CardRequest BuildCardRequest(CardDetails card)
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

    private static Address? BuildAddress(CardDetails card)
    {
        if (string.IsNullOrWhiteSpace(card.CountryCode))
        {
            return null;
        }

        return new Address
        {
            AddressLine1 = card.AddressLine1,
            AddressLine2 = card.AddressLine2,
            AdminArea2 = card.City,
            AdminArea1 = card.State,
            PostalCode = card.PostalCode,
            CountryCode = card.CountryCode
        };
    }

    private static IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> ChunkRange(DateTimeOffset from, DateTimeOffset to)
    {
        var start = from;
        while (start < to)
        {
            var end = start.AddDays(31) < to ? start.AddDays(31) : to;
            yield return (start, end);
            start = end;
        }
    }

    private static string Format(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string FormatRfc3339(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static decimal? ParseMoney(Money? money) =>
        money?.Value is not null && decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static DateTimeOffset? ParseTime(object? value) => value switch
    {
        null => null,
        DateTimeOffset dto => dto,
        string s when DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) => parsed,
        _ => null
    };

    private PaymentGatewayException MapCreateOrderError(SdkException<CreateOrderError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return MapTypedError(error.Name, error.Message, error.Details?.FirstOrDefault()?.Issue, ex);
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRawError(raw, ex);
        }
        return Unknown(ex);
    }

    // Error.Name / Details.Issue are plain strings (no enums are modeled); map deliberately.
    private PaymentGatewayException MapTypedError(string? name, string? message, string? issue, Exception ex)
    {
        _logger.LogWarning("PayPal rejected a request: {Name} {Issue} {Message}", name, issue, message);

        var kind = PaymentGatewayErrorKind.Validation;
        if (name?.Contains("NOT_FOUND", StringComparison.OrdinalIgnoreCase) == true)
        {
            kind = PaymentGatewayErrorKind.NotFound;
        }
        else if (name?.Contains("CONFLICT", StringComparison.OrdinalIgnoreCase) == true ||
                 issue?.Contains("ALREADY", StringComparison.OrdinalIgnoreCase) == true)
        {
            kind = PaymentGatewayErrorKind.Conflict;
        }

        return new PaymentGatewayException(kind, $"PayPal rejected the request: {message ?? name ?? "unknown error"}", null, ex);
    }

    private PaymentGatewayException MapRawError(RawError raw, Exception ex)
    {
        var status = (int)raw.StatusCode;
        _logger.LogWarning("PayPal returned HTTP {StatusCode}: {Body}", status, raw.ReadAsString());

        var kind = status switch
        {
            404 => PaymentGatewayErrorKind.NotFound,
            409 => PaymentGatewayErrorKind.Conflict,
            >= 400 and < 500 => PaymentGatewayErrorKind.Validation,
            _ => PaymentGatewayErrorKind.Unavailable
        };

        return new PaymentGatewayException(kind, $"PayPal returned HTTP {status}.", status, ex);
    }

    private static PaymentGatewayException Unavailable(Exception ex) =>
        new(PaymentGatewayErrorKind.Unavailable, "The payment provider could not be reached.", null, ex);

    private static PaymentGatewayException Unprocessable(JsonException ex) =>
        new(PaymentGatewayErrorKind.Unavailable, "The payment provider returned a response that could not be processed.", null, ex);

    private static PaymentGatewayException Unknown(Exception ex) =>
        new(PaymentGatewayErrorKind.Unknown, "The payment provider returned an unexpected error.", null, ex);
}
