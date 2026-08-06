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
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using DomainCardDetails = Microsoft.eShopWeb.ApplicationCore.Payments.CardDetails;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// PayPal implementation of <see cref="IPaymentGateway"/> over the PayPalServerSdk. Translates every
/// provider, transport, and deserialization failure into a caller-safe
/// <see cref="PaymentGatewayException"/>; PayPal/SDK internals and card data never leak out. All
/// write operations forward the caller's idempotency key as PayPal-Request-Id so a retry replays the
/// original outcome instead of charging or refunding twice.
/// </summary>
public class PayPalPaymentGateway : IPaymentGateway
{
    // Ask PayPal to return the full order/refund representation so the nested capture/refund ids are
    // present on the response.
    private const string PreferRepresentation = "return=representation";

    private readonly PayPalServerSdkClient _client;
    private readonly ILogger<PayPalPaymentGateway> _logger;

    public PayPalPaymentGateway(PayPalServerSdkClient client, ILogger<PayPalPaymentGateway> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<PaymentCaptureResult> ChargeCardAsync(decimal amount, string currency, DomainCardDetails card,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var orderRequest = BuildOrderRequest(amount, currency, new CardRequest
        {
            Number = card.Number,
            Expiry = card.ExpiryWireValue,
            SecurityCode = card.SecurityCode,
            Name = card.CardholderName,
            BillingAddress = ToSdkAddress(card.BillingAddress)
        });

        return await CreateAndCaptureAsync(orderRequest, idempotencyKey, cancellationToken);
    }

    public async Task<PaymentCaptureResult> ChargeVaultedCardAsync(decimal amount, string currency, string vaultId,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var orderRequest = BuildOrderRequest(amount, currency, new CardRequest { VaultId = vaultId });
        return await CreateAndCaptureAsync(orderRequest, idempotencyKey, cancellationToken);
    }

    public async Task<PaymentRefundResult> RefundAsync(string captureId, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Full refund: an empty (null) body.
            var refund = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: null,
                prefer: PreferRepresentation,
                ct: cancellationToken);

            if (string.IsNullOrEmpty(refund.Id))
            {
                throw new PaymentGatewayException("PayPal did not return a refund id.", HttpStatusCode.BadGateway);
            }

            var status = refund.Status?.Value ?? "UNKNOWN";
            return new PaymentRefundResult(refund.Id, status);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            throw TranslateOrdersError(ex.Error, "refund the payment");
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            throw ProviderUnavailable(ex);
        }
        catch (JsonException ex)
        {
            throw UnprocessableResponse(ex);
        }
    }

    public async Task<VaultedCardResult> VaultCardAsync(DomainCardDetails card, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var request = new PaymentTokenRequest
        {
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Number = card.Number,
                    Expiry = card.ExpiryWireValue,
                    SecurityCode = card.SecurityCode,
                    Name = card.CardholderName,
                    BillingAddress = ToSdkAddress(card.BillingAddress)
                }
            }
        };

        try
        {
            var response = await _client.Vault.CreatePaymentToken(
                payPalRequestId: idempotencyKey,
                body: request,
                ct: cancellationToken);

            if (string.IsNullOrEmpty(response.Id))
            {
                throw new PaymentGatewayException("PayPal did not return a vault id for the card.",
                    HttpStatusCode.BadGateway);
            }

            var cardEntity = response.PaymentSource?.Card;
            var brand = cardEntity?.Brand?.Value ?? "CARD";
            var lastDigits = cardEntity?.LastDigits ?? "0000";
            var expiry = cardEntity?.Expiry ?? card.ExpiryWireValue;

            return new VaultedCardResult(response.Id, brand, lastDigits, expiry);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            throw TranslateVaultError(ex.Error, "save the card");
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            throw ProviderUnavailable(ex);
        }
        catch (JsonException ex)
        {
            throw UnprocessableResponse(ex);
        }
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(id: vaultId, ct: cancellationToken);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            throw TranslateVaultError(ex.Error, "delete the saved card");
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            throw ProviderUnavailable(ex);
        }
        catch (JsonException ex)
        {
            throw UnprocessableResponse(ex);
        }
    }

    // --- create + capture --------------------------------------------------------------------------

    private async Task<PaymentCaptureResult> CreateAndCaptureAsync(OrderRequest orderRequest, string idempotencyKey,
        CancellationToken cancellationToken)
    {
        PayPalServerSdk.Models.Order order;
        try
        {
            order = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: orderRequest,
                prefer: PreferRepresentation,
                ct: cancellationToken);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw TranslateOrdersError(ex.Error, "create the payment");
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            throw ProviderUnavailable(ex);
        }
        catch (JsonException ex)
        {
            throw UnprocessableResponse(ex);
        }

        // A CAPTURE-intent order with a card payment source may auto-capture on creation (status
        // COMPLETED, capture already nested) or require an explicit capture. Branch on the status so a
        // second capture is never attempted (that would throw ORDER_ALREADY_CAPTURED).
        if (order.Status != OrderStatus.Completed)
        {
            try
            {
                order = await _client.Orders.CaptureOrder(
                    id: order.Id,
                    payPalMockResponse: null,
                    payPalRequestId: idempotencyKey + "-capture",
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: null,
                    prefer: PreferRepresentation,
                    ct: cancellationToken);
            }
            catch (SdkException<CaptureOrderError> ex)
            {
                throw TranslateOrdersError(ex.Error, "capture the payment");
            }
            catch (Exception ex) when (IsTransportFailure(ex))
            {
                throw ProviderUnavailable(ex);
            }
            catch (JsonException ex)
            {
                throw UnprocessableResponse(ex);
            }
        }

        return ExtractCapture(order);
    }

    private static PaymentCaptureResult ExtractCapture(PayPalServerSdk.Models.Order order)
    {
        var capture = order.PurchaseUnits?.FirstOrDefault()?.Payments?.Captures?.FirstOrDefault();
        if (capture is null || string.IsNullOrEmpty(capture.Id))
        {
            throw new PaymentGatewayException("PayPal did not return a capture for the order.",
                HttpStatusCode.BadGateway);
        }

        var status = capture.Status?.Value ?? "UNKNOWN";
        if (capture.Status != CaptureStatus.Completed)
        {
            // The request was well-formed but the funds did not move (e.g. the card was declined).
            throw new PaymentGatewayException($"The payment was not completed (status: {status}).",
                HttpStatusCode.PaymentRequired);
        }

        return new PaymentCaptureResult(order.Id ?? string.Empty, capture.Id, status);
    }

    // --- request building --------------------------------------------------------------------------

    private static OrderRequest BuildOrderRequest(decimal amount, string currency, CardRequest card) =>
        new()
        {
            Intent = CheckoutPaymentIntent.Capture,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new()
                {
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = currency,
                        Value = amount.ToString("F2", CultureInfo.InvariantCulture)
                    }
                }
            },
            PaymentSource = new PaymentSource { Card = card }
        };

    private static Address ToSdkAddress(BillingAddress? billing) =>
        new()
        {
            AddressLine1 = billing?.AddressLine1,
            AddressLine2 = billing?.AddressLine2,
            AdminArea2 = billing?.AdminArea2,
            AdminArea1 = billing?.AdminArea1,
            PostalCode = billing?.PostalCode,
            CountryCode = billing?.CountryCode ?? "US"
        };

    // --- error translation -------------------------------------------------------------------------

    private PaymentGatewayException TranslateOrdersError(CreateOrderError error, string action) =>
        TranslateFromApiError(error.TryGetError, error.TryGetRawError, action);

    private PaymentGatewayException TranslateOrdersError(CaptureOrderError error, string action) =>
        TranslateFromApiError(error.TryGetError, error.TryGetRawError, action);

    private PaymentGatewayException TranslateOrdersError(RefundCapturedPaymentError error, string action) =>
        TranslateFromApiError(error.TryGetError, error.TryGetRawError, action);

    private PaymentGatewayException TranslateVaultError(CreatePaymentTokenError error, string action) =>
        TranslateFromApiError1(error.TryGetError1, error.TryGetRawError, action);

    private PaymentGatewayException TranslateVaultError(DeletePaymentTokenError error, string action) =>
        TranslateFromApiError1(error.TryGetError1, error.TryGetRawError, action);

    private delegate bool TryGetTyped(out Error error);
    private delegate bool TryGetTyped1(out Error1 error);
    private delegate bool TryGetRaw(out RawError raw);

    private PaymentGatewayException TranslateFromApiError(TryGetTyped tryGetTyped, TryGetRaw tryGetRaw, string action)
    {
        if (tryGetTyped(out var typed))
        {
            _logger.LogWarning("PayPal failed to {Action}: {Name} (debugId {DebugId})",
                action, typed.Name, typed.DebugId);
            return new PaymentGatewayException($"Could not {action}: {typed.Name}.", ClassifyPayPalError(typed.Name));
        }

        return FromRaw(tryGetRaw, action);
    }

    private PaymentGatewayException TranslateFromApiError1(TryGetTyped1 tryGetTyped, TryGetRaw tryGetRaw, string action)
    {
        if (tryGetTyped(out var typed))
        {
            _logger.LogWarning("PayPal failed to {Action}: {Name} (debugId {DebugId})",
                action, typed.Name, typed.DebugId);
            return new PaymentGatewayException($"Could not {action}: {typed.Name}.", ClassifyPayPalError(typed.Name));
        }

        return FromRaw(tryGetRaw, action);
    }

    private PaymentGatewayException FromRaw(TryGetRaw tryGetRaw, string action)
    {
        if (tryGetRaw(out var raw))
        {
            var code = (int)raw.StatusCode;
            _logger.LogWarning("PayPal failed to {Action} with HTTP {Code}", action, code);
            // A 4xx is caller-actionable and surfaces as that same status; anything else is a provider
            // problem the caller cannot fix, surfaced as 502.
            var status = code is >= 400 and < 500 ? raw.StatusCode : HttpStatusCode.BadGateway;
            return new PaymentGatewayException($"Could not {action} (provider returned HTTP {code}).", status);
        }

        return new PaymentGatewayException($"Could not {action}.", HttpStatusCode.BadGateway);
    }

    // PayPal names that indicate a provider/config fault (not caller-actionable) map to 502; all other
    // typed errors are caller-actionable (declines, validation, resource state) and map to 400.
    private static HttpStatusCode ClassifyPayPalError(string? name) => name?.ToUpperInvariant() switch
    {
        null or "" => HttpStatusCode.BadGateway,
        "INTERNAL_SERVER_ERROR" => HttpStatusCode.BadGateway,
        "SERVICE_UNAVAILABLE" => HttpStatusCode.BadGateway,
        "AUTHENTICATION_FAILURE" => HttpStatusCode.BadGateway,
        "NOT_AUTHORIZED" => HttpStatusCode.BadGateway,
        "PERMISSION_DENIED" => HttpStatusCode.BadGateway,
        _ => HttpStatusCode.BadRequest
    };

    private static bool IsTransportFailure(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or OperationCanceledException;

    private PaymentGatewayException ProviderUnavailable(Exception ex)
    {
        _logger.LogWarning(ex, "PayPal request failed at the transport layer.");
        return new PaymentGatewayException("The payment provider is currently unavailable. Please try again.",
            HttpStatusCode.BadGateway, ex);
    }

    private PaymentGatewayException UnprocessableResponse(JsonException ex)
    {
        _logger.LogWarning(ex, "PayPal returned a response that could not be processed.");
        return new PaymentGatewayException("The payment provider returned a response that could not be processed.",
            HttpStatusCode.BadGateway, ex);
    }
}
