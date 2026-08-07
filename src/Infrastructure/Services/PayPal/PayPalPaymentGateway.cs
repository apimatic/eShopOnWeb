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
using Microsoft.eShopWeb.ApplicationCore.Payments;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// The only code that talks to the PayPal SDK. It maps the app's payment operations onto PayPal
/// Orders / Payments / Vault calls, and translates every SDK failure into a single caller-safe
/// <see cref="PaymentGatewayException"/> (never leaking SDK/provider text). Idempotency keys flow
/// through as <c>PayPal-Request-Id</c> so retries never double-charge or double-refund.
/// </summary>
public class PayPalPaymentGateway : IPayPalPaymentGateway
{
    private readonly PayPalServerSdkClient _client;

    public PayPalPaymentGateway(PayPalServerSdkClient client)
    {
        _client = client;
    }

    public Task<PaymentCaptureResult> ChargeCardAsync(decimal amount, string currencyCode, CardDetails card,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var paymentSource = new PaymentSource
        {
            Card = new CardRequest
            {
                Number = card.Number,
                Expiry = card.Expiry,
                SecurityCode = card.SecurityCode,
                Name = card.CardholderName,
                BillingAddress = ToSdkAddress(card.BillingAddress)
            }
        };
        return CreateAndCaptureAsync(amount, currencyCode, paymentSource, idempotencyKey, cancellationToken);
    }

    public Task<PaymentCaptureResult> ChargeVaultedCardAsync(decimal amount, string currencyCode, string vaultId,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var paymentSource = new PaymentSource
        {
            // Pay with a saved card: reference the vault token; leave the raw card fields unset.
            Card = new CardRequest { VaultId = vaultId }
        };
        return CreateAndCaptureAsync(amount, currencyCode, paymentSource, idempotencyKey, cancellationToken);
    }

    private async Task<PaymentCaptureResult> CreateAndCaptureAsync(decimal amount, string currencyCode,
        PaymentSource paymentSource, string idempotencyKey, CancellationToken cancellationToken)
    {
        const string action = "payment";
        try
        {
            var orderRequest = new OrderRequest
            {
                Intent = CheckoutPaymentIntent.Capture,
                PurchaseUnits = new List<PurchaseUnitRequest>
                {
                    new()
                    {
                        Amount = new AmountWithBreakdown
                        {
                            CurrencyCode = currencyCode,
                            Value = FormatAmount(amount)
                        }
                    }
                },
                PaymentSource = paymentSource
            };

            // return=representation so the nested capture id is present in the response body.
            var createdOrder = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: orderRequest,
                prefer: "return=representation",
                ct: cancellationToken);

            var payPalOrderId = createdOrder.Id
                ?? throw new PaymentGatewayException("PayPal did not return an order id.", 502);

            // Defensive per ⚠-D: only capture when the create did not already complete it.
            var settledOrder = createdOrder;
            if (createdOrder.Status != OrderStatus.Completed)
            {
                settledOrder = await _client.Orders.CaptureOrder(
                    id: payPalOrderId,
                    payPalMockResponse: null,
                    payPalRequestId: idempotencyKey + "-capture",
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: null,
                    prefer: "return=representation",
                    ct: cancellationToken);
            }

            var capture = TryExtractCapture(settledOrder)
                ?? throw new PaymentGatewayException("PayPal did not return a capture for the payment.", 502);

            return new PaymentCaptureResult(payPalOrderId, capture.CaptureId, capture.Status);
        }
        catch (SdkException<CreateOrderError> ex) { throw Translate(ex, action); }
        catch (SdkException<CaptureOrderError> ex) { throw Translate(ex, action); }
        catch (SdkException<RawError> ex) { throw FromRawError(ex.Error, action); }
        catch (JsonException ex) { throw Unreadable(action, ex); }
        catch (Exception ex) when (IsTransport(ex, cancellationToken)) { throw Unreachable(action, ex); }
    }

    public async Task<VaultedCardResult> VaultCardAsync(CardDetails card, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        const string action = "card save";
        try
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
                        Name = card.CardholderName,
                        BillingAddress = ToSdkAddress(card.BillingAddress)
                    }
                }
            };

            var response = await _client.Vault.CreatePaymentToken(
                payPalRequestId: idempotencyKey,
                body: request,
                ct: cancellationToken);

            var vaultId = response.Id
                ?? throw new PaymentGatewayException("PayPal did not return a vault token id.", 502);

            var storedCard = response.PaymentSource?.Card;
            var brand = storedCard?.Brand?.Value ?? "CARD";
            var last4 = storedCard?.LastDigits ?? card.Last4;

            return new VaultedCardResult(vaultId, brand, last4, card.Expiry);
        }
        catch (SdkException<CreatePaymentTokenError> ex) { throw Translate(ex, action); }
        catch (SdkException<RawError> ex) { throw FromRawError(ex.Error, action); }
        catch (JsonException ex) { throw Unreadable(action, ex); }
        catch (Exception ex) when (IsTransport(ex, cancellationToken)) { throw Unreachable(action, ex); }
    }

    public async Task<RefundResult> RefundCaptureAsync(string captureId, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        const string action = "refund";
        try
        {
            // body: null → full refund of the capture.
            var refund = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: null,
                prefer: "return=representation",
                ct: cancellationToken);

            var refundId = refund.Id
                ?? throw new PaymentGatewayException("PayPal did not return a refund id.", 502);

            return new RefundResult(refundId, refund.Status?.Value ?? string.Empty);
        }
        catch (SdkException<RefundCapturedPaymentError> ex) { throw Translate(ex, action); }
        catch (SdkException<RawError> ex) { throw FromRawError(ex.Error, action); }
        catch (JsonException ex) { throw Unreadable(action, ex); }
        catch (Exception ex) when (IsTransport(ex, cancellationToken)) { throw Unreachable(action, ex); }
    }

    // --- helpers ---

    private static string FormatAmount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static Address? ToSdkAddress(CardBillingAddress? billing)
    {
        if (billing is null)
        {
            return null;
        }
        return new Address
        {
            AddressLine1 = billing.AddressLine1,
            AddressLine2 = billing.AddressLine2,
            AdminArea2 = billing.City,
            AdminArea1 = billing.State,
            PostalCode = billing.PostalCode,
            CountryCode = billing.CountryCode
        };
    }

    private static (string CaptureId, string Status)? TryExtractCapture(Order order)
    {
        var capture = order.PurchaseUnits?
            .FirstOrDefault()?.Payments?
            .Captures?.FirstOrDefault();

        if (capture?.Id is null)
        {
            return null;
        }
        return (capture.Id, capture.Status?.Value ?? string.Empty);
    }

    private static bool IsTransport(Exception ex, CancellationToken ct) =>
        (ex is HttpRequestException || ex is TaskCanceledException) && !ct.IsCancellationRequested;

    private static PaymentGatewayException Unreadable(string action, Exception ex) =>
        new($"PayPal returned a {action} response that could not be processed.", 502, ex);

    private static PaymentGatewayException Unreachable(string action, Exception ex) =>
        new($"PayPal was unreachable while processing the {action}.", 502, ex);

    private static PaymentGatewayException FromRawError(RawError raw, string action)
    {
        var status = (int)raw.StatusCode;
        return new PaymentGatewayException($"PayPal could not process the {action} (HTTP {status}).", status);
    }

    // Typed-error branch carries no numeric status; PayPal folds a 500 into it for some ops,
    // detectable only via the error name. Everything else here is a client-actionable rejection.
    private static PaymentGatewayException FromTypedError(string? name, string? message, string action)
    {
        var isServer = string.Equals(name, "INTERNAL_SERVER_ERROR", StringComparison.OrdinalIgnoreCase);
        var status = isServer ? 500 : 422;
        return new PaymentGatewayException($"PayPal rejected the {action}: {name} - {message}.", status);
    }

    private static PaymentGatewayException Translate(SdkException<CreateOrderError> ex, string action)
    {
        if (ex.Error.TryGetError(out var e)) return FromTypedError(e.Name, e.Message, action);
        if (ex.Error.TryGetRawError(out var raw)) return FromRawError(raw, action);
        return new PaymentGatewayException($"PayPal could not process the {action}.", 502, ex);
    }

    private static PaymentGatewayException Translate(SdkException<CaptureOrderError> ex, string action)
    {
        if (ex.Error.TryGetError(out var e)) return FromTypedError(e.Name, e.Message, action);
        if (ex.Error.TryGetRawError(out var raw)) return FromRawError(raw, action);
        return new PaymentGatewayException($"PayPal could not process the {action}.", 502, ex);
    }

    private static PaymentGatewayException Translate(SdkException<RefundCapturedPaymentError> ex, string action)
    {
        if (ex.Error.TryGetError(out var e)) return FromTypedError(e.Name, e.Message, action);
        if (ex.Error.TryGetNoContent(out var noContent)) return FromRawError(noContent, action); // 500
        if (ex.Error.TryGetRawError(out var raw)) return FromRawError(raw, action);
        return new PaymentGatewayException($"PayPal could not process the {action}.", 502, ex);
    }

    private static PaymentGatewayException Translate(SdkException<CreatePaymentTokenError> ex, string action)
    {
        if (ex.Error.TryGetError1(out var e)) return FromTypedError(e.Name, e.Message, action);
        if (ex.Error.TryGetRawError(out var raw)) return FromRawError(raw, action);
        return new PaymentGatewayException($"PayPal could not process the {action}.", 502, ex);
    }
}
