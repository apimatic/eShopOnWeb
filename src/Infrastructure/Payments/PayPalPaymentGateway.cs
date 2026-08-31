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
using Microsoft.Extensions.Options;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalAddress = PayPalServerSdk.Models.Address;
using PayPalError = PayPalServerSdk.Models.Error;
using PayPalError1 = PayPalServerSdk.Models.Error1;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// PayPal implementation of <see cref="IPaymentGateway"/> over the PayPalServerSdk client.
/// Full card details pass through to PayPal only; they are never logged or persisted here.
/// </summary>
public class PayPalPaymentGateway : IPaymentGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(60);

    private readonly PayPalServerSdk.PayPalServerSdkClient _client;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalPaymentGateway> _logger;

    public PayPalPaymentGateway(PayPalServerSdk.PayPalServerSdkClient client,
        IOptions<PayPalOptions> options, ILogger<PayPalPaymentGateway> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public string Currency => _options.Currency ?? string.Empty;

    public async Task<GatewayOrderResult> CreateOrderAsync(string idempotencyKey, decimal amount,
        string currency, string customId, CancellationToken ct)
    {
        // No invoice id here: the merchant account blocks a duplicate invoice id per
        // transaction, and an invoice id set on the order makes the subsequent authorize
        // count as a second use. The invoice id is attached at capture time instead.
        var body = new OrderRequest
        {
            Intent = PayPalServerSdk.Models.Enums.CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = currency,
                        Value = Format(amount)
                    },
                    CustomId = customId
                }
            }
        };

        try
        {
            var order = await Bounded(c => _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: body,
                ct: c), ct);

            return new GatewayOrderResult
            {
                PayPalOrderId = order.Id ?? string.Empty,
                Status = order.Status?.Value
            };
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw Translate("create-order", ex);
        }
        catch (JsonException ex)
        {
            throw Malformed("create-order", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable("create-order", ex);
        }
    }

    public async Task<GatewayAuthorizationResult> AuthorizeWithCardAsync(string payPalOrderId,
        string idempotencyKey, CardPaymentDetails card, CancellationToken ct)
    {
        var body = new OrderAuthorizeRequest
        {
            PaymentSource = new OrderAuthorizeRequestPaymentSource
            {
                Card = new CardRequest
                {
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    Name = card.Name,
                    BillingAddress = MapAddress(card.BillingAddress)
                }
            }
        };

        return await AuthorizeAsync(payPalOrderId, idempotencyKey, body, ct);
    }

    public async Task<GatewayAuthorizationResult> AuthorizeWithVaultedCardAsync(string payPalOrderId,
        string idempotencyKey, string vaultTokenId, CancellationToken ct)
    {
        var body = new OrderAuthorizeRequest
        {
            PaymentSource = new OrderAuthorizeRequestPaymentSource
            {
                Card = new CardRequest
                {
                    VaultId = vaultTokenId
                }
            }
        };

        return await AuthorizeAsync(payPalOrderId, idempotencyKey, body, ct);
    }

    private async Task<GatewayAuthorizationResult> AuthorizeAsync(string payPalOrderId,
        string idempotencyKey, OrderAuthorizeRequest body, CancellationToken ct)
    {
        try
        {
            var response = await Bounded(c => _client.Orders.AuthorizeOrder(
                id: payPalOrderId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: c), ct);

            var authorization = response.PurchaseUnits?
                .SelectMany(p => p.Payments?.Authorizations ?? Array.Empty<AuthorizationWithAdditionalData>())
                .FirstOrDefault();

            if (authorization?.Id is null)
            {
                throw Malformed("authorize-order",
                    new InvalidOperationException("PayPal's authorize response contained no authorization."));
            }

            return new GatewayAuthorizationResult
            {
                PayPalOrderId = response.Id ?? payPalOrderId,
                AuthorizationId = authorization.Id,
                Status = authorization.Status?.Value,
                StatusReason = authorization.StatusDetails?.Reason?.Value,
                ExpirationTime = ParseDate(authorization.ExpirationTime)
            };
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            throw Translate("authorize-order", ex);
        }
        catch (JsonException ex)
        {
            throw Malformed("authorize-order", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable("authorize-order", ex);
        }
    }

    public async Task<GatewayAuthorizationInfo> GetAuthorizationAsync(string authorizationId, CancellationToken ct)
    {
        try
        {
            var authorization = await Bounded(c => _client.Payments.GetAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                ct: c), ct);

            return Map(authorization);
        }
        catch (SdkException<GetAuthorizedPaymentError> ex)
        {
            throw Translate("get-authorization", ex);
        }
        catch (JsonException ex)
        {
            throw Malformed("get-authorization", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable("get-authorization", ex);
        }
    }

    public async Task<GatewayAuthorizationInfo> ReauthorizeAsync(string authorizationId,
        string idempotencyKey, decimal amount, string currency, CancellationToken ct)
    {
        var body = new ReauthorizeRequest
        {
            Amount = new Money { CurrencyCode = currency, Value = Format(amount) }
        };

        try
        {
            var authorization = await Bounded(c => _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: c), ct);

            return Map(authorization);
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            throw Translate("reauthorize-payment", ex);
        }
        catch (JsonException ex)
        {
            throw Malformed("reauthorize-payment", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable("reauthorize-payment", ex);
        }
    }

    public async Task<GatewayCaptureResult> CaptureAsync(string authorizationId, string idempotencyKey,
        decimal amount, string currency, string invoiceId, CancellationToken ct)
    {
        var body = new CaptureRequest
        {
            Amount = new Money { CurrencyCode = currency, Value = Format(amount) },
            FinalCapture = true,
            InvoiceId = invoiceId
        };

        try
        {
            var capture = await Bounded(c => _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: c), ct);

            return new GatewayCaptureResult
            {
                CaptureId = capture.Id ?? string.Empty,
                Status = capture.Status?.Value,
                StatusReason = capture.StatusDetails?.Reason?.Value,
                GrossAmount = ParseMoney(capture.SellerReceivableBreakdown?.GrossAmount) ?? ParseMoney(capture.Amount),
                SellerFee = ParseMoney(capture.SellerReceivableBreakdown?.PaypalFee),
                NetAmount = ParseMoney(capture.SellerReceivableBreakdown?.NetAmount)
            };
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            throw Translate("capture-payment", ex);
        }
        catch (JsonException ex)
        {
            throw Malformed("capture-payment", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable("capture-payment", ex);
        }
    }

    public async Task<GatewayAuthorizationInfo> VoidAsync(string authorizationId, string idempotencyKey,
        CancellationToken ct)
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

            return Map(authorization);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            throw Translate("void-payment", ex);
        }
        catch (JsonException ex)
        {
            throw Malformed("void-payment", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable("void-payment", ex);
        }
    }

    public async Task<GatewayRefundResult> RefundAsync(string captureId, string idempotencyKey,
        decimal? amount, string currency, string customId, CancellationToken ct)
    {
        // A null amount refunds the capture in full (empty payload per the operation doc).
        // The capture's invoice id must NOT be reused here (the merchant account rejects it
        // as a duplicate); the payment join key travels as custom_id instead.
        RefundRequest? body = amount is null
            ? new RefundRequest { CustomId = customId }
            : new RefundRequest
            {
                Amount = new Money { CurrencyCode = currency, Value = Format(amount.Value) },
                CustomId = customId
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

            return new GatewayRefundResult
            {
                RefundId = refund.Id ?? string.Empty,
                Status = refund.Status?.Value,
                Amount = ParseMoney(refund.Amount)
            };
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            throw Translate("refund-payment", ex);
        }
        catch (JsonException ex)
        {
            throw Malformed("refund-payment", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable("refund-payment", ex);
        }
    }

    public async Task<GatewayVaultedCard> VaultCardAsync(string idempotencyKey, string shopperKey,
        CardPaymentDetails card, CancellationToken ct)
    {
        var body = new PaymentTokenRequest
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
            Customer = new Customer { MerchantCustomerId = shopperKey }
        };

        try
        {
            var token = await Bounded(c => _client.Vault.CreatePaymentToken(
                payPalRequestId: idempotencyKey,
                body: body,
                ct: c), ct);

            if (token.Id is null)
            {
                throw Malformed("vault-card",
                    new InvalidOperationException("PayPal's vault response contained no payment token id."));
            }

            return new GatewayVaultedCard
            {
                VaultTokenId = token.Id,
                PayPalCustomerId = token.Customer?.Id,
                Brand = token.PaymentSource?.Card?.Brand?.Value,
                LastDigits = token.PaymentSource?.Card?.LastDigits,
                Expiry = token.PaymentSource?.Card?.Expiry,
                CardholderName = token.PaymentSource?.Card?.Name
            };
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            throw Translate("vault-card", ex);
        }
        catch (JsonException ex)
        {
            throw Malformed("vault-card", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable("vault-card", ex);
        }
    }

    public async Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken ct)
    {
        try
        {
            await Bounded(c => _client.Vault.DeletePaymentToken(
                id: vaultTokenId,
                ct: c), ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            throw Translate("delete-vaulted-card", ex);
        }
        catch (JsonException ex)
        {
            throw Malformed("delete-vaulted-card", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable("delete-vaulted-card", ex);
        }
    }

    public async Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken ct)
    {
        var results = new List<GatewayTransaction>();

        // The reporting API accepts at most a 31-day range per call: chunk longer windows.
        foreach (var (sliceStart, sliceEnd) in Chunk(from, to, TimeSpan.FromDays(31)))
        {
            var page = 1;
            var totalPages = 1;
            while (page <= totalPages)
            {
                SearchResponse response;
                try
                {
                    response = await Bounded(c => _client.TransactionSearch.SearchTransactions(
                        startDate: FormatDate(sliceStart),
                        endDate: FormatDate(sliceEnd),
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
                        ct: c), ct);
                }
                catch (SdkException<RawError> ex)
                {
                    throw Raw("search-transactions", ex.Error);
                }
                catch (JsonException ex)
                {
                    throw Malformed("search-transactions", ex);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    throw Unreachable("search-transactions", ex);
                }

                totalPages = response.TotalPages ?? 1;

                foreach (var detail in response.TransactionDetails ?? Array.Empty<TransactionDetails>())
                {
                    var info = detail.TransactionInfo;
                    if (info is null)
                    {
                        continue;
                    }

                    results.Add(new GatewayTransaction
                    {
                        TransactionId = info.TransactionId,
                        ReferenceId = info.PaypalReferenceId,
                        ReferenceIdType = info.PaypalReferenceIdType?.Value,
                        Status = info.TransactionStatus,
                        Amount = ParseMoney(info.TransactionAmount),
                        Currency = info.TransactionAmount?.CurrencyCode,
                        Fee = ParseMoney(info.FeeAmount),
                        InvoiceId = info.InvoiceId,
                        CustomField = info.CustomField,
                        EventCode = info.TransactionEventCode,
                        Time = ParseDate(info.TransactionInitiationDate)
                    });
                }

                page++;
            }
        }

        return results;
    }

    private static GatewayAuthorizationInfo Map(PaymentAuthorization authorization) => new()
    {
        AuthorizationId = authorization.Id ?? string.Empty,
        Status = authorization.Status?.Value,
        ExpirationTime = ParseDate(authorization.ExpirationTime)
    };

    private static PayPalAddress? MapAddress(ApplicationCore.Entities.OrderAggregate.Address? address)
    {
        if (address is null)
        {
            return null;
        }

        return new PayPalAddress
        {
            CountryCode = address.Country,
            AddressLine1 = address.Street,
            AdminArea2 = address.City,
            AdminArea1 = address.State,
            PostalCode = address.ZipCode
        };
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

    private static string Format(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string FormatDate(DateTimeOffset value) =>
        value.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;

    private static decimal? ParseMoney(Money? money) =>
        money?.Value is not null && decimal.TryParse(money.Value, NumberStyles.Number,
            CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> Chunk(
        DateTimeOffset from, DateTimeOffset to, TimeSpan maxSlice)
    {
        var start = from;
        while (start < to)
        {
            var end = start + maxSlice < to ? start + maxSlice : to;
            yield return (start, end);
            start = end;
        }
    }

    // ---- Error boundary -----------------------------------------------------
    // One translation per operation error type: every typed accessor is enumerated,
    // with TryGetRawError last. Typed bodies mean the provider rejected the request
    // (all typed statuses for these operations are 4xx); RawError carries the status.

    private PaymentGatewayException Translate(string operation, SdkException<CreateOrderError> ex)
    {
        if (ex.Error.TryGetError(out var e)) return Rejection(operation, e);
        if (ex.Error.TryGetRawError(out var raw)) return Raw(operation, raw);
        return Unknown(operation, ex);
    }

    private PaymentGatewayException Translate(string operation, SdkException<AuthorizeOrderError> ex)
    {
        if (ex.Error.TryGetError(out var e)) return Rejection(operation, e);
        if (ex.Error.TryGetRawError(out var raw)) return Raw(operation, raw);
        return Unknown(operation, ex);
    }

    private PaymentGatewayException Translate(string operation, SdkException<GetAuthorizedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var e)) return Rejection(operation, e);
        if (ex.Error.TryGetNoContent(out var raw)) return Raw(operation, raw);
        if (ex.Error.TryGetRawError(out var fallback)) return Raw(operation, fallback);
        return Unknown(operation, ex);
    }

    private PaymentGatewayException Translate(string operation, SdkException<ReauthorizePaymentError> ex)
    {
        if (ex.Error.TryGetError(out var e)) return Rejection(operation, e);
        if (ex.Error.TryGetRawError(out var raw)) return Raw(operation, raw);
        return Unknown(operation, ex);
    }

    private PaymentGatewayException Translate(string operation, SdkException<CaptureAuthorizedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var e)) return Rejection(operation, e);
        if (ex.Error.TryGetNoContent(out var raw)) return Raw(operation, raw);
        if (ex.Error.TryGetRawError(out var fallback)) return Raw(operation, fallback);
        return Unknown(operation, ex);
    }

    private PaymentGatewayException Translate(string operation, SdkException<VoidPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var e)) return Rejection(operation, e);
        if (ex.Error.TryGetNoContent(out var raw)) return Raw(operation, raw);
        if (ex.Error.TryGetRawError(out var fallback)) return Raw(operation, fallback);
        return Unknown(operation, ex);
    }

    private PaymentGatewayException Translate(string operation, SdkException<RefundCapturedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var e)) return Rejection(operation, e);
        if (ex.Error.TryGetNoContent(out var raw)) return Raw(operation, raw);
        if (ex.Error.TryGetRawError(out var fallback)) return Raw(operation, fallback);
        return Unknown(operation, ex);
    }

    private PaymentGatewayException Translate(string operation, SdkException<CreatePaymentTokenError> ex)
    {
        if (ex.Error.TryGetError1(out var e)) return Rejection(operation, e);
        if (ex.Error.TryGetRawError(out var raw)) return Raw(operation, raw);
        return Unknown(operation, ex);
    }

    private PaymentGatewayException Translate(string operation, SdkException<DeletePaymentTokenError> ex)
    {
        if (ex.Error.TryGetError1(out var e)) return Rejection(operation, e);
        if (ex.Error.TryGetRawError(out var raw)) return Raw(operation, raw);
        return Unknown(operation, ex);
    }

    private PaymentGatewayException Rejection(string operation, PayPalError error)
    {
        _logger.LogWarning("PayPal {Operation} rejected the request: {Name} (debug id {DebugId}).",
            operation, error.Name, error.DebugId);
        return new PaymentGatewayException(
            $"PayPal {operation} rejected the request: {error.Name} — {error.Message}",
            isProviderRejection: true,
            errorName: error.Name,
            debugId: error.DebugId,
            issues: error.Details?.Select(d => $"{d.Issue}: {d.Description}").ToList());
    }

    private PaymentGatewayException Rejection(string operation, PayPalError1 error)
    {
        _logger.LogWarning("PayPal {Operation} rejected the request: {Name} (debug id {DebugId}).",
            operation, error.Name, error.DebugId);
        return new PaymentGatewayException(
            $"PayPal {operation} rejected the request: {error.Name} — {error.Message}",
            isProviderRejection: true,
            errorName: error.Name,
            debugId: error.DebugId,
            issues: error.Details?.Select(d => $"{d.Issue}: {d.Description}").ToList());
    }

    private PaymentGatewayException Raw(string operation, RawError raw)
    {
        var status = (int)raw.StatusCode;
        _logger.LogWarning("PayPal {Operation} failed with HTTP {StatusCode}.", operation, status);
        return new PaymentGatewayException(
            $"PayPal {operation} failed with HTTP {status}.",
            providerStatusCode: status,
            isProviderRejection: status >= 400 && status < 500);
    }

    private PaymentGatewayException Malformed(string operation, Exception ex)
    {
        _logger.LogError(ex, "PayPal {Operation} returned a response that could not be processed.", operation);
        return new PaymentGatewayException(
            $"PayPal {operation} returned a response that could not be processed.",
            innerException: ex);
    }

    private PaymentGatewayException Unreachable(string operation, Exception ex)
    {
        _logger.LogError(ex, "PayPal {Operation} could not reach the payment provider.", operation);
        return new PaymentGatewayException(
            $"PayPal {operation}: the payment provider could not be reached. The operation may or may not have completed; check the payment state before retrying.",
            innerException: ex);
    }

    private PaymentGatewayException Unknown(string operation, Exception ex)
    {
        _logger.LogError(ex, "PayPal {Operation} failed with an unreadable error response.", operation);
        return new PaymentGatewayException(
            $"PayPal {operation} failed with an unreadable error response.",
            innerException: ex);
    }
}
