using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
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
using PayPalOrder = PayPalServerSdk.Models.Order;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// IPaymentGateway over the PayPal Server SDK. All PayPal contract details live here;
/// card details pass through transiently and are never logged.
/// </summary>
public class PayPalPaymentGateway : IPaymentGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(60);

    private readonly PayPalServerSdkClient _client;

    public PayPalPaymentGateway(PayPalServerSdkClient client)
    {
        _client = client;
    }

    public async Task<GatewayAuthorization> AuthorizeAsync(GatewayAuthorizeRequest request, CancellationToken ct = default)
    {
        var paymentSource = request.Card is not null
            ? new PaymentSource { Card = ToCardRequest(request.Card) }
            : new PaymentSource
            {
                Token = new Token
                {
                    Id = request.PaymentTokenId!,
                    Type = TokenType.FromValue("PAYMENT_METHOD_TOKEN")
                }
            };

        var body = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = request.Currency,
                        Value = FormatAmount(request.Amount)
                    },
                    InvoiceId = request.InvoiceId,
                    CustomId = request.InvoiceId
                }
            },
            PaymentSource = paymentSource
        };

        try
        {
            var order = await Bounded(c => _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: request.IdempotencyKey,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: c), ct);

            if (order.Status == OrderStatus.PayerActionRequired)
            {
                throw new PayerActionRequiredException(
                    "PayPal requires the shopper to approve this payment in a browser " +
                    "(a 3-D Secure style challenge). This integration does not support an approval round-trip.");
            }

            var authorization = order.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
            if (authorization is null)
            {
                // Some flows create the order without authorizing inline; authorize explicitly.
                var authorizeResponse = await Bounded(c => _client.Orders.AuthorizeOrder(
                    id: order.Id!,
                    payPalMockResponse: null,
                    payPalRequestId: request.IdempotencyKey,
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: null,
                    prefer: "return=representation",
                    ct: c), ct);
                authorization = authorizeResponse.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
            }

            if (authorization?.Id is null)
            {
                throw new PaymentGatewayException("PayPal did not return an authorization for the order.");
            }

            if (authorization.Status == AuthorizationStatus.Denied)
            {
                throw new PaymentDeclinedException(DescribeProcessorResponse(authorization.ProcessorResponse));
            }

            return new GatewayAuthorization(
                order.Id!,
                authorization.Id,
                authorization.Status?.Value ?? "UNKNOWN",
                ParseAmount(authorization.Amount) ?? request.Amount,
                request.Currency,
                ParseDate(authorization.ExpirationTime));
        }
        catch (SdkException<CreateOrderError> ex)
        {
            if (ex.Error.TryGetError(out var e)) throw Rejection("create order", e);
            if (ex.Error.TryGetRawError(out var raw)) throw RawFailure("create order", raw);
            throw new PaymentGatewayException("PayPal create order failed.");
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            if (ex.Error.TryGetError(out var e)) throw Rejection("authorize order", e);
            if (ex.Error.TryGetRawError(out var raw)) throw RawFailure("authorize order", raw);
            throw new PaymentGatewayException("PayPal authorize order failed.");
        }
        catch (JsonException ex)
        {
            throw new PaymentGatewayException("PayPal returned a response that could not be processed.", innerException: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentGatewayException("PayPal is unreachable or timed out.", innerException: ex);
        }
    }

    public async Task<GatewayCapture> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            var capture = await Bounded(c => _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: new CaptureRequest { FinalCapture = true },
                prefer: "return=representation",
                ct: c), ct);

            if (capture.Status == CaptureStatus.Declined)
            {
                throw new PaymentDeclinedException(DescribeProcessorResponse(capture.ProcessorResponse));
            }

            // CapturedPayment.Amount is authoritative for the captured amount;
            // the breakdown carries fee/net (absent for pending captures - null-checked).
            var breakdown = capture.SellerReceivableBreakdown;
            return new GatewayCapture(
                capture.Id!,
                capture.Status?.Value ?? "UNKNOWN",
                ParseAmount(capture.Amount) ?? ParseAmount(breakdown?.GrossAmount) ?? 0m,
                ParseAmount(breakdown?.PaypalFee),
                ParseAmount(breakdown?.NetAmount),
                capture.Amount?.CurrencyCode ?? breakdown?.GrossAmount?.CurrencyCode ?? string.Empty);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var e)) throw Rejection("capture", e);
            if (ex.Error.TryGetNoContent(out var noContent)) throw RawFailure("capture", noContent);
            if (ex.Error.TryGetRawError(out var raw)) throw RawFailure("capture", raw);
            throw new PaymentGatewayException("PayPal capture failed.");
        }
        catch (JsonException ex)
        {
            throw new PaymentGatewayException("PayPal returned a response that could not be processed.", innerException: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentGatewayException("PayPal is unreachable or timed out.", innerException: ex);
        }
    }

    public async Task<GatewayAuthorizationState> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default)
    {
        try
        {
            var authorization = await Bounded(c => _client.Payments.GetAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                ct: c), ct);
            return ToState(authorization);
        }
        catch (SdkException<GetAuthorizedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var e)) throw Rejection("get authorization", e);
            if (ex.Error.TryGetNoContent(out var noContent)) throw RawFailure("get authorization", noContent);
            if (ex.Error.TryGetRawError(out var raw)) throw RawFailure("get authorization", raw);
            throw new PaymentGatewayException("PayPal get authorization failed.");
        }
        catch (JsonException ex)
        {
            throw new PaymentGatewayException("PayPal returned a response that could not be processed.", innerException: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentGatewayException("PayPal is unreachable or timed out.", innerException: ex);
        }
    }

    public async Task<GatewayAuthorizationState> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            var authorization = await Bounded(c => _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest
                {
                    Amount = new Money { CurrencyCode = currency, Value = FormatAmount(amount) }
                },
                prefer: "return=representation",
                ct: c), ct);
            return ToState(authorization);
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            if (ex.Error.TryGetError(out var e)) throw Rejection("reauthorize", e);
            if (ex.Error.TryGetNoContent(out var noContent)) throw RawFailure("reauthorize", noContent);
            if (ex.Error.TryGetRawError(out var raw)) throw RawFailure("reauthorize", raw);
            throw new PaymentGatewayException("PayPal reauthorize failed.");
        }
        catch (JsonException ex)
        {
            throw new PaymentGatewayException("PayPal returned a response that could not be processed.", innerException: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentGatewayException("PayPal is unreachable or timed out.", innerException: ex);
        }
    }

    public async Task<GatewayAuthorizationState> VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            var authorization = await Bounded(c => _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: idempotencyKey,
                prefer: "return=representation",
                ct: c), ct);
            return ToState(authorization);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var e)) throw Rejection("void", e);
            if (ex.Error.TryGetNoContent(out var noContent)) throw RawFailure("void", noContent);
            if (ex.Error.TryGetRawError(out var raw)) throw RawFailure("void", raw);
            throw new PaymentGatewayException("PayPal void failed.");
        }
        catch (JsonException ex)
        {
            throw new PaymentGatewayException("PayPal returned a response that could not be processed.", innerException: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentGatewayException("PayPal is unreachable or timed out.", innerException: ex);
        }
    }

    public async Task<GatewayRefund> RefundAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken ct = default)
    {
        // Full refund: the API wants an empty payload. Partial: an explicit amount.
        var body = amount is null
            ? new RefundRequest()
            : new RefundRequest
            {
                Amount = new Money { CurrencyCode = currency, Value = FormatAmount(amount.Value) }
            };

        try
        {
            var refund = await Bounded(c => _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: c), ct);

            return new GatewayRefund(
                refund.Id!,
                refund.Status?.Value ?? "UNKNOWN",
                ParseAmount(refund.Amount),
                refund.Amount?.CurrencyCode);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var e)) throw Rejection("refund", e);
            if (ex.Error.TryGetNoContent(out var noContent)) throw RawFailure("refund", noContent);
            if (ex.Error.TryGetRawError(out var raw)) throw RawFailure("refund", raw);
            throw new PaymentGatewayException("PayPal refund failed.");
        }
        catch (JsonException ex)
        {
            throw new PaymentGatewayException("PayPal returned a response that could not be processed.", innerException: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentGatewayException("PayPal is unreachable or timed out.", innerException: ex);
        }
    }

    public async Task<GatewayVaultedCard> VaultCardAsync(GatewayCardDetails card, string merchantCustomerId, string idempotencyKey, CancellationToken ct = default)
    {
        var body = new PaymentTokenRequest
        {
            Customer = new Customer { MerchantCustomerId = merchantCustomerId },
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    Name = card.Name,
                    BillingAddress = ToAddress(card.BillingAddress)
                }
            }
        };

        try
        {
            var token = await Bounded(c => _client.Vault.CreatePaymentToken(
                payPalRequestId: idempotencyKey,
                body: body,
                ct: c), ct);

            if (token.Id is null)
            {
                throw new PaymentGatewayException("PayPal did not return a payment token for the card.");
            }

            var cardEntity = token.PaymentSource?.Card;
            return new GatewayVaultedCard(
                token.Id,
                token.Customer?.Id ?? merchantCustomerId,
                cardEntity?.LastDigits,
                cardEntity?.Brand?.Value,
                cardEntity?.Expiry);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError1(out var e)) throw Rejection("vault card", e);
            if (ex.Error.TryGetRawError(out var raw)) throw RawFailure("vault card", raw);
            throw new PaymentGatewayException("PayPal vault card failed.");
        }
        catch (JsonException ex)
        {
            throw new PaymentGatewayException("PayPal returned a response that could not be processed.", innerException: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentGatewayException("PayPal is unreachable or timed out.", innerException: ex);
        }
    }

    public async Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken ct = default)
    {
        try
        {
            await Bounded(c => _client.Vault.DeletePaymentToken(
                id: paymentTokenId,
                ct: c), ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError1(out var e)) throw Rejection("delete saved card", e);
            if (ex.Error.TryGetRawError(out var raw))
            {
                // A token PayPal no longer knows is as good as deleted.
                if (raw.StatusCode == HttpStatusCode.NotFound) return;
                throw RawFailure("delete saved card", raw);
            }
            throw new PaymentGatewayException("PayPal delete saved card failed.");
        }
        catch (JsonException ex)
        {
            throw new PaymentGatewayException("PayPal returned a response that could not be processed.", innerException: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentGatewayException("PayPal is unreachable or timed out.", innerException: ex);
        }
    }

    public async Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        // The API supports a maximum range of 31 days per call - chunk longer windows.
        var results = new List<GatewayTransaction>();
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart.AddDays(31) < to ? windowStart.AddDays(31) : to;
            results.AddRange(await SearchTransactionsWindowAsync(windowStart, windowEnd, ct));
            windowStart = windowEnd;
        }
        return results;
    }

    private async Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsWindowAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var results = new List<GatewayTransaction>();
        var page = 1;
        var totalPages = 1;

        try
        {
            do
            {
                var response = await Bounded(c => _client.TransactionSearch.SearchTransactions(
                    startDate: from.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
                    endDate: to.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
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
                    ct: c), ct);

                totalPages = response.TotalPages ?? 1;
                if (response.TransactionDetails is not null)
                {
                    foreach (var detail in response.TransactionDetails)
                    {
                        var info = detail.TransactionInfo;
                        if (info is null) continue;
                        results.Add(new GatewayTransaction(
                            info.TransactionId,
                            info.TransactionEventCode,
                            info.TransactionStatus,
                            ParseAmount(info.TransactionAmount),
                            info.TransactionAmount?.CurrencyCode,
                            ParseAmount(info.FeeAmount),
                            info.InvoiceId,
                            info.CustomField,
                            ParseDate(info.TransactionInitiationDate),
                            ParseDate(info.TransactionUpdatedDate)));
                    }
                }
                page++;
            }
            while (page <= totalPages);
        }
        catch (SdkException<RawError> ex)
        {
            throw RawFailure("transaction search", ex.Error);
        }
        catch (JsonException ex)
        {
            throw new PaymentGatewayException("PayPal returned a response that could not be processed.", innerException: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentGatewayException("PayPal is unreachable or timed out.", innerException: ex);
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

    private static CardRequest ToCardRequest(GatewayCardDetails card) => new CardRequest
    {
        Number = card.Number,
        Expiry = card.Expiry,
        SecurityCode = card.SecurityCode,
        Name = card.Name,
        BillingAddress = ToAddress(card.BillingAddress)
    };

    private static PayPalAddress? ToAddress(GatewayBillingAddress? address) => address is null
        ? null
        : new PayPalAddress
        {
            CountryCode = address.CountryCode,
            AddressLine1 = address.AddressLine1,
            AddressLine2 = address.AddressLine2,
            AdminArea2 = address.City,
            AdminArea1 = address.State,
            PostalCode = address.PostalCode
        };

    private static GatewayAuthorizationState ToState(PaymentAuthorization authorization) => new GatewayAuthorizationState(
        authorization.Id!,
        authorization.Status?.Value ?? "UNKNOWN",
        ParseAmount(authorization.Amount),
        authorization.Amount?.CurrencyCode,
        ParseDate(authorization.ExpirationTime));

    private static string FormatAmount(decimal amount) => amount.ToString("F2", CultureInfo.InvariantCulture);

    private static decimal? ParseAmount(Money? money) =>
        money?.Value is not null && decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;

    private static string DescribeProcessorResponse(ProcessorResponse? processorResponse) =>
        processorResponse?.ResponseCode is not null
            ? $"The card was declined (processor response {processorResponse.ResponseCode.Value})."
            : "The card was declined.";

    private static PaymentGatewayException Rejection(string operation, Error error)
    {
        var issues = error.Details is null
            ? string.Empty
            : " [" + string.Join("; ", error.Details.Select(d => $"{d.Issue}: {d.Description}")) + "]";
        return new PaymentGatewayException(
            $"PayPal {operation} rejected the request: {error.Name} - {error.Message}{issues}",
            isProviderRejection: true,
            providerErrorName: error.Name);
    }

    private static PaymentGatewayException Rejection(string operation, Error1 error)
    {
        var issues = error.Details is null
            ? string.Empty
            : " [" + string.Join("; ", error.Details.Select(d => $"{d.Issue}: {d.Description}")) + "]";
        return new PaymentGatewayException(
            $"PayPal {operation} rejected the request: {error.Name} - {error.Message}{issues}",
            isProviderRejection: true,
            providerErrorName: error.Name);
    }

    private static PaymentGatewayException RawFailure(string operation, RawError raw)
    {
        var body = raw.ReadAsString();
        if (body.Length > 500) body = body.Substring(0, 500);
        return new PaymentGatewayException(
            $"PayPal {operation} failed with HTTP {(int)raw.StatusCode}: {body}",
            isProviderRejection: (int)raw.StatusCode >= 400 && (int)raw.StatusCode < 500);
    }
}
