using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
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
/// IPaymentGateway over the PayPal Server SDK. Every write passes a stable PayPal-Request-Id so
/// provider-side de-duplication makes client retries safe. All calls are bounded by a total
//  budget; provider 4xx statuses are carried on PaymentGatewayException.ProviderStatusCode.
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

    public Task<AuthorizationResult> AuthorizeAsync(AuthorizePaymentRequest request, CancellationToken ct)
        => Bounded(async token =>
        {
            var payPalOrderId = request.ExistingPayPalOrderId ?? await CreateOrderAsync(request, token);
            return await AuthorizeOrderAsync(request, payPalOrderId, token);
        }, ct);

    public Task<GatewayAuthorizationStatus> GetAuthorizationAsync(string authorizationId, CancellationToken ct)
        => Bounded(async token =>
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
                    authorization.Status.WireValue() ?? "UNKNOWN",
                    ParseDate(authorization.ExpirationTime),
                    ParseMoney(authorization.Amount));
            }
            catch (SdkException<GetAuthorizedPaymentError> ex)
            {
                if (ex.Error.TryGetError(out var error))
                {
                    throw FromPayPalError(ex.Error, error.Name, error.Message, error.DebugId, ex);
                }
                if (ex.Error.TryGetNoContent(out var noContent))
                {
                    throw FromNoContent(noContent, ex);
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw FromRawError(raw, ex);
                }
                throw UnknownProviderError("get the authorization", ex);
            }
        }, ct);

    public Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string requestKey, CancellationToken ct)
        => Bounded(async token =>
        {
            try
            {
                var renewed = await _client.Payments.ReauthorizePayment(
                    authorizationId: authorizationId,
                    payPalRequestId: requestKey,
                    payPalAuthAssertion: null,
                    body: new ReauthorizeRequest
                    {
                        Amount = new Money
                        {
                            CurrencyCode = currency,
                            Value = FormatMoney(amount)
                        }
                    },
                    prefer: "return=representation",
                    ct: token);

                return new AuthorizationResult(
                    string.Empty,
                    renewed.Id ?? authorizationId,
                    renewed.Status.WireValue() ?? "UNKNOWN",
                    ParseDate(renewed.ExpirationTime),
                    ParseMoney(renewed.Amount) ?? amount,
                    currency,
                    null);
            }
            catch (SdkException<ReauthorizePaymentError> ex)
            {
                if (ex.Error.TryGetError(out var error))
                {
                    throw FromPayPalError(ex.Error, error.Name, error.Message, error.DebugId, ex);
                }
                if (ex.Error.TryGetNoContent(out var noContent))
                {
                    throw FromNoContent(noContent, ex);
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw FromRawError(raw, ex);
                }
                throw UnknownProviderError("reauthorize the payment", ex);
            }
        }, ct);

    public Task<CaptureResult> CaptureAsync(string authorizationId, string requestKey, CancellationToken ct)
        => Bounded(async token =>
        {
            try
            {
                var capture = await _client.Payments.CaptureAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalRequestId: requestKey,
                    payPalAuthAssertion: null,
                    body: new CaptureRequest { FinalCapture = true },
                    prefer: "return=representation",
                    ct: token);

                var amount = ParseMoney(capture.Amount)
                    ?? throw new PaymentGatewayException("PayPal did not return the captured amount.");
                var breakdown = capture.SellerReceivableBreakdown;

                return new CaptureResult(
                    capture.Id ?? throw new PaymentGatewayException("PayPal did not return a capture id."),
                    capture.Status.WireValue() ?? "UNKNOWN",
                    amount,
                    ParseMoney(breakdown?.PaypalFee),
                    ParseMoney(breakdown?.NetAmount),
                    capture.Amount?.CurrencyCode ?? string.Empty);
            }
            catch (SdkException<CaptureAuthorizedPaymentError> ex)
            {
                if (ex.Error.TryGetError(out var error))
                {
                    throw FromPayPalError(ex.Error, error.Name, error.Message, error.DebugId, ex);
                }
                if (ex.Error.TryGetNoContent(out var noContent))
                {
                    throw FromNoContent(noContent, ex);
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw FromRawError(raw, ex);
                }
                throw UnknownProviderError("capture the payment", ex);
            }
        }, ct);

    public Task VoidAsync(string authorizationId, string requestKey, CancellationToken ct)
        => Bounded(async token =>
        {
            try
            {
                await _client.Payments.VoidPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    payPalRequestId: requestKey,
                    prefer: "return=representation",
                    ct: token);
            }
            catch (SdkException<VoidPaymentError> ex)
            {
                if (ex.Error.TryGetError(out var error))
                {
                    throw FromPayPalError(ex.Error, error.Name, error.Message, error.DebugId, ex);
                }
                if (ex.Error.TryGetNoContent(out var noContent))
                {
                    throw FromNoContent(noContent, ex);
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw FromRawError(raw, ex);
                }
                throw UnknownProviderError("void the authorization", ex);
            }
        }, ct);

    public Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency, string requestKey, string? noteToPayer, CancellationToken ct)
        => Bounded(async token =>
        {
            try
            {
                var refund = await _client.Payments.RefundCapturedPayment(
                    captureId: captureId,
                    payPalMockResponse: null,
                    payPalRequestId: requestKey,
                    payPalAuthAssertion: null,
                    body: amount.HasValue
                        ? new RefundRequest
                        {
                            Amount = new Money { CurrencyCode = currency, Value = FormatMoney(amount.Value) },
                            NoteToPayer = noteToPayer
                        }
                        : new RefundRequest { NoteToPayer = noteToPayer },
                    prefer: "return=representation",
                    ct: token);

                return new RefundResult(
                    refund.Id ?? throw new PaymentGatewayException("PayPal did not return a refund id."),
                    refund.Status.WireValue() ?? "UNKNOWN",
                    ParseMoney(refund.Amount) ?? amount ?? 0m,
                    ParseMoney(refund.SellerPayableBreakdown?.TotalRefundedAmount),
                    refund.Amount?.CurrencyCode ?? currency);
            }
            catch (SdkException<RefundCapturedPaymentError> ex)
            {
                if (ex.Error.TryGetError(out var error))
                {
                    throw FromPayPalError(ex.Error, error.Name, error.Message, error.DebugId, ex);
                }
                if (ex.Error.TryGetNoContent(out var noContent))
                {
                    throw FromNoContent(noContent, ex);
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw FromRawError(raw, ex);
                }
                throw UnknownProviderError("refund the payment", ex);
            }
        }, ct);

    private async Task<string> CreateOrderAsync(AuthorizePaymentRequest request, CancellationToken ct)
    {
        var body = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    ReferenceId = request.LocalOrderId.ToString(CultureInfo.InvariantCulture),
                    // custom_id echoes back as custom_field in transaction reports; the unique
                    // invoice id keeps reconciliation exact even on a shared merchant account.
                    CustomId = ApplicationCore.Entities.PaymentAggregate.PaymentInvoiceId.For(request.LocalOrderId, request.CreateRequestKey),
                    InvoiceId = ApplicationCore.Entities.PaymentAggregate.PaymentInvoiceId.For(request.LocalOrderId, request.CreateRequestKey),
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = request.Currency,
                        Value = FormatMoney(request.Amount)
                    }
                }
            }
        };

        try
        {
            var order = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: request.CreateRequestKey,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: ct);

            return order.Id ?? throw new PaymentGatewayException("PayPal did not return an order id.");
        }
        catch (SdkException<CreateOrderError> ex)
        {
            if (ex.Error.TryGetError(out var error))
            {
                throw FromPayPalError(ex.Error, error.Name, error.Message, error.DebugId, ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRawError(raw, ex);
            }
            throw UnknownProviderError("create the order", ex);
        }
    }

    private async Task<AuthorizationResult> AuthorizeOrderAsync(AuthorizePaymentRequest request, string payPalOrderId, CancellationToken ct)
    {
        var body = new OrderAuthorizeRequest
        {
            PaymentSource = new OrderAuthorizeRequestPaymentSource
            {
                Card = BuildCardRequest(request)
            }
        };

        try
        {
            var response = await _client.Orders.AuthorizeOrder(
                id: payPalOrderId,
                payPalMockResponse: null,
                payPalRequestId: request.AuthorizeRequestKey,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: ct);

            if (response.Status == OrderStatus.PayerActionRequired)
            {
                throw new PaymentGatewayException(
                    "PayPal requires a browser approval step for this card (PAYER_ACTION_REQUIRED), which this integration does not support.");
            }

            var authorization = response.PurchaseUnits?
                .SelectMany(unit => unit.Payments?.Authorizations ?? Enumerable.Empty<AuthorizationWithAdditionalData>())
                .FirstOrDefault();

            if (authorization?.Id == null)
            {
                throw new PaymentGatewayException("PayPal did not return an authorization for the order.");
            }

            var status = authorization.Status.WireValue() ?? "UNKNOWN";
            if (authorization.Status == AuthorizationStatus.Denied)
            {
                var reason = authorization.StatusDetails?.Reason.WireValue()
                    ?? authorization.ProcessorResponse?.ResponseCode.WireValue()
                    ?? "DENIED";
                return new AuthorizationResult(payPalOrderId, authorization.Id, status, null, request.Amount, request.Currency, reason);
            }
            if (authorization.Status != AuthorizationStatus.Created)
            {
                throw new PaymentGatewayException($"PayPal returned an unexpected authorization status '{status}'.");
            }

            return new AuthorizationResult(
                payPalOrderId,
                authorization.Id,
                status,
                ParseDate(authorization.ExpirationTime),
                ParseMoney(authorization.Amount) ?? request.Amount,
                authorization.Amount?.CurrencyCode ?? request.Currency,
                null);
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            if (ex.Error.TryGetError(out var error))
            {
                throw FromPayPalError(ex.Error, error.Name, error.Message, error.DebugId, ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRawError(raw, ex);
            }
            throw UnknownProviderError("authorize the order", ex);
        }
    }

    private static CardRequest BuildCardRequest(AuthorizePaymentRequest request)
    {
        if (request.VaultPaymentTokenId != null)
        {
            return new CardRequest { VaultId = request.VaultPaymentTokenId };
        }

        var card = request.Card
            ?? throw new PaymentGatewayException("No payment source was supplied for the authorization.");

        return new CardRequest
        {
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            Name = card.Name,
            BillingAddress = BuildAddress(card.BillingAddress)
        };
    }

    internal static Address? BuildAddress(BillingAddressDetails? address)
        => address == null
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

    internal static string FormatMoney(decimal amount)
        => amount.ToString("F2", CultureInfo.InvariantCulture);

    internal static decimal? ParseMoney(Money? money)
        => money?.Value != null && decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    internal static DateTimeOffset? ParseDate(string? value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;

    private PaymentGatewayException FromPayPalError(ApiError apiError, string name, string message, string? debugId, Exception inner)
    {
        // The AsyncLocal status can be lost across the SDK's error path; the typed error's raw
        // payload still carries the HTTP status.
        var status = PayPalResponseStatusTracker.LastStatus
            ?? (apiError.TryGetRawError(out var raw) ? (int?)raw.StatusCode : null);
        _logger.LogWarning("PayPal rejected the request: {Name} {Message} (debug id {DebugId}, HTTP {Status})",
            name, message, debugId, status);
        return new PaymentGatewayException($"PayPal error {name}: {message} (debug id: {debugId})", status, debugId, inner);
    }

    private PaymentGatewayException FromNoContent(RawError raw, Exception inner)
    {
        _logger.LogWarning("PayPal failed the request with HTTP {Status} and no error body", (int)raw.StatusCode);
        return new PaymentGatewayException($"PayPal failed the request (HTTP {(int)raw.StatusCode}).", (int)raw.StatusCode, null, inner);
    }

    private PaymentGatewayException FromRawError(RawError raw, Exception inner)
    {
        _logger.LogWarning("PayPal rejected the request with HTTP {Status}", (int)raw.StatusCode);
        return new PaymentGatewayException($"PayPal rejected the request (HTTP {(int)raw.StatusCode}).", (int)raw.StatusCode, null, inner);
    }

    private PaymentGatewayException UnknownProviderError(string operation, Exception inner)
        => new PaymentGatewayException($"PayPal could not {operation}; the failure could not be classified.",
            PayPalResponseStatusTracker.LastStatus, null, inner);

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
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
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw new PaymentGatewayException("The payment provider did not respond within the allowed time.", null, null, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new PaymentGatewayException("The payment provider could not be reached.", null, null, ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new PaymentGatewayException("The payment provider returned a response that could not be processed.",
                PayPalResponseStatusTracker.LastStatus, null, ex);
        }
    }

    private async Task Bounded(Func<CancellationToken, Task> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        try
        {
            await call(cts.Token);
        }
        catch (PaymentGatewayException)
        {
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw new PaymentGatewayException("The payment provider did not respond within the allowed time.", null, null, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new PaymentGatewayException("The payment provider could not be reached.", null, null, ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new PaymentGatewayException("The payment provider returned a response that could not be processed.",
                PayPalResponseStatusTracker.LastStatus, null, ex);
        }
    }
}
