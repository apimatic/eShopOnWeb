using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;   // RawError
using PayPalServerSdk.Core.Exceptions;      // SdkException<TError>
using PayPalServerSdk.Errors;               // per-operation {Operation}Error types
using PayPalServerSdk.Models;               // request/response records
using PayPalServerSdk.Models.Enums;         // CheckoutPaymentIntent

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// The only class in the solution that talks to the PayPal SDK. Translates the application-layer
/// <see cref="IPayPalGateway"/> contract into PayPal Orders / Payments / Vault calls and converts every
/// SDK failure into a caller-safe <see cref="PaymentFailedException"/>. Card data is never logged and never
/// placed into any exception message.
/// </summary>
public class PayPalGateway : IPayPalGateway
{
    private const string StatusCompleted = "COMPLETED";
    private const string StatusPending = "PENDING";

    private readonly PayPalServerSdkClient _client;
    private readonly IAppLogger<PayPalGateway>? _logger;

    public PayPalGateway(PayPalServerSdkClient client, IAppLogger<PayPalGateway>? logger = null)
    {
        _client = client;
        _logger = logger;
    }

    public Task<PayPalCaptureResult> CaptureWithCardAsync(decimal amount, string currency, CardDetails card, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var paymentSource = new PaymentSource
        {
            Card = new CardRequest
            {
                Number = card.Number,
                Expiry = card.Expiry,
                SecurityCode = card.SecurityCode,
                Name = card.CardholderName,
                BillingAddress = MapAddress(card.BillingAddress)
            }
        };
        return CreateCaptureAndExtractAsync(amount, currency, paymentSource, idempotencyKey, cancellationToken);
    }

    public Task<PayPalCaptureResult> CaptureWithVaultedCardAsync(decimal amount, string currency, string vaultId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var paymentSource = new PaymentSource
        {
            // Reference a saved card by its vault id — NOT the Token payment-source (that is for PayPal
            // billing agreements, whose only TokenType is BILLING_AGREEMENT).
            Card = new CardRequest { VaultId = vaultId }
        };
        return CreateCaptureAndExtractAsync(amount, currency, paymentSource, idempotencyKey, cancellationToken);
    }

    public async Task<VaultedCardResult> VaultCardAsync(string customerId, CardDetails card, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new PaymentTokenRequest
        {
            Customer = new Customer { MerchantCustomerId = customerId },
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    Name = card.CardholderName,
                    BillingAddress = MapAddress(card.BillingAddress)
                }
            }
        };

        try
        {
            PaymentTokenResponse response = await _client.Vault.CreatePaymentToken(
                payPalRequestId: idempotencyKey,
                body: body,
                ct: cancellationToken);

            var vaultedCard = response.PaymentSource?.Card;   // CardPaymentTokenEntity
            var result = new VaultedCardResult
            {
                VaultId = response.Id ?? string.Empty,
                Brand = vaultedCard?.Brand?.Value,
                Last4 = vaultedCard?.LastDigits,
                Expiry = vaultedCard?.Expiry
            };
            _logger?.LogInformation("PayPal card vaulted. VaultId={VaultId} Brand={Brand} Last4={Last4}",
                result.VaultId, result.Brand ?? "?", result.Last4 ?? "?");
            return result;
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            // Vault operations expose the typed payload via TryGetError1 (note the "1"); the fallback is last.
            if (ex.Error.TryGetError1(out var err))
                throw Fail("CreatePaymentToken", err.Name, err.Message, err.DebugId, ex);
            if (ex.Error.TryGetRawError(out var raw))
                throw FailRaw("CreatePaymentToken", raw, ex);
            throw FailUnknown("CreatePaymentToken", ex);
        }
        catch (JsonException ex)
        {
            throw FailUnreadable("CreatePaymentToken", ex);
        }
        catch (Exception ex) when (IsTransport(ex, cancellationToken))
        {
            throw FailTransport("CreatePaymentToken", ex);
        }
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(id: vaultId, ct: cancellationToken);
            _logger?.LogInformation("PayPal vaulted card deleted. VaultId={VaultId}", vaultId);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            // DeletePaymentToken has no typed 404 accessor — a not-found surfaces on the raw fallback.
            // Swallow it so delete is idempotent; rethrow anything else.
            if (ex.Error.TryGetError1(out var err))
                throw Fail("DeletePaymentToken", err.Name, err.Message, err.DebugId, ex);
            if (ex.Error.TryGetRawError(out var raw))
            {
                if (raw.StatusCode == HttpStatusCode.NotFound)
                {
                    _logger?.LogInformation("PayPal vaulted card already absent; treating delete as success. VaultId={VaultId}", vaultId);
                    return;
                }
                throw FailRaw("DeletePaymentToken", raw, ex);
            }
            throw FailUnknown("DeletePaymentToken", ex);
        }
        catch (JsonException ex)
        {
            throw FailUnreadable("DeletePaymentToken", ex);
        }
        catch (Exception ex) when (IsTransport(ex, cancellationToken))
        {
            throw FailTransport("DeletePaymentToken", ex);
        }
    }

    public async Task<RefundResult> RefundCaptureAsync(string captureId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        try
        {
            Refund refund = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: null,   // null body = full refund
                ct: cancellationToken);

            var result = new RefundResult
            {
                RefundId = refund.Id ?? string.Empty,
                Status = refund.Status?.Value ?? string.Empty
            };
            _logger?.LogInformation("PayPal refund issued. RefundId={RefundId} Status={Status}",
                result.RefundId, result.Status);
            return result;
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var err))
                throw Fail("RefundCapturedPayment", err.Name, err.Message, err.DebugId, ex);
            if (ex.Error.TryGetNoContent(out var noContent))
                throw FailRaw("RefundCapturedPayment", noContent, ex);
            if (ex.Error.TryGetRawError(out var raw))
                throw FailRaw("RefundCapturedPayment", raw, ex);
            throw FailUnknown("RefundCapturedPayment", ex);
        }
        catch (JsonException ex)
        {
            throw FailUnreadable("RefundCapturedPayment", ex);
        }
        catch (Exception ex) when (IsTransport(ex, cancellationToken))
        {
            throw FailTransport("RefundCapturedPayment", ex);
        }
    }

    /// <summary>
    /// Create an order with intent=CAPTURE for the given payment source, then — since inline-card
    /// auto-capture is not guaranteed — capture it if the create response is not already COMPLETED, and
    /// extract the resulting capture id/status.
    /// </summary>
    private async Task<PayPalCaptureResult> CreateCaptureAndExtractAsync(
        decimal amount, string currency, PaymentSource paymentSource, string idempotencyKey, CancellationToken cancellationToken)
    {
        var orderBody = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Capture,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = currency,
                        Value = amount.ToString("F2", CultureInfo.InvariantCulture)
                    }
                }
            },
            PaymentSource = paymentSource
        };

        try
        {
            Order order = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: orderBody,
                ct: cancellationToken);

            if (!IsCompleted(order.Status))
            {
                var orderId = order.Id
                    ?? throw new PaymentFailedException("PayPal CreateOrder returned no order id to capture.");

                // Same idempotency key so a retried capture cannot double-charge.
                order = await _client.Orders.CaptureOrder(
                    id: orderId,
                    payPalMockResponse: null,
                    payPalRequestId: idempotencyKey,
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: null,
                    ct: cancellationToken);
            }

            var capture = order.PurchaseUnits?
                .SelectMany(pu => pu.Payments?.Captures ?? Enumerable.Empty<OrdersCapture>())
                .FirstOrDefault();

            if (capture?.Id is null)
                throw new PaymentFailedException($"PayPal capture did not complete for order {order.Id}.");

            var status = capture.Status?.Value ?? string.Empty;
            if (!IsAcceptableCaptureStatus(status))
                throw new PaymentFailedException($"PayPal capture for order {order.Id} was not successful (status: {status}).");

            _logger?.LogInformation("PayPal capture succeeded. OrderId={OrderId} CaptureId={CaptureId} Status={Status}",
                order.Id ?? "?", capture.Id, status);

            return new PayPalCaptureResult
            {
                PayPalOrderId = order.Id ?? string.Empty,
                CaptureId = capture.Id,
                Status = status
            };
        }
        catch (SdkException<CreateOrderError> ex)
        {
            if (ex.Error.TryGetError(out var err))
                throw Fail("CreateOrder", err.Name, err.Message, err.DebugId, ex);
            if (ex.Error.TryGetRawError(out var raw))
                throw FailRaw("CreateOrder", raw, ex);
            throw FailUnknown("CreateOrder", ex);
        }
        catch (SdkException<CaptureOrderError> ex)
        {
            if (ex.Error.TryGetError(out var err))
                throw Fail("CaptureOrder", err.Name, err.Message, err.DebugId, ex);
            if (ex.Error.TryGetRawError(out var raw))
                throw FailRaw("CaptureOrder", raw, ex);
            throw FailUnknown("CaptureOrder", ex);
        }
        catch (JsonException ex)
        {
            throw FailUnreadable("CreateOrder/CaptureOrder", ex);
        }
        catch (Exception ex) when (IsTransport(ex, cancellationToken))
        {
            throw FailTransport("CreateOrder/CaptureOrder", ex);
        }
    }

    private static Address? MapAddress(BillingAddressDetails? billing)
    {
        if (billing is null)
            return null;

        return new Address
        {
            AddressLine1 = billing.AddressLine1,
            AddressLine2 = billing.AddressLine2,
            AdminArea2 = billing.City,     // city    -> admin_area_2
            AdminArea1 = billing.State,    // state   -> admin_area_1
            PostalCode = billing.PostalCode,
            CountryCode = billing.CountryCode   // required by PayPal for card processing
        };
    }

    private static bool IsCompleted(OrderStatus? status) =>
        string.Equals(status?.Value, StatusCompleted, StringComparison.OrdinalIgnoreCase);

    private static bool IsAcceptableCaptureStatus(string status) =>
        string.Equals(status, StatusCompleted, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, StatusPending, StringComparison.OrdinalIgnoreCase);

    // A caller-triggered cancellation is not a provider failure — let it propagate as cancellation.
    private static bool IsTransport(Exception ex, CancellationToken ct) =>
        !ct.IsCancellationRequested &&
        (ex is System.Net.Http.HttpRequestException || ex is TaskCanceledException || ex is OperationCanceledException);

    // Typed provider error (deterministic rejection): surface PayPal's own name/message/debug_id, all safe.
    private PaymentFailedException Fail(string op, string? name, string? message, string? debugId, Exception inner)
    {
        _logger?.LogWarning("PayPal {Op} rejected. Name={Name} DebugId={DebugId}", op, name ?? "?", debugId ?? "?");
        return new PaymentFailedException($"PayPal {op} failed: {name} - {message} (debug_id: {debugId})", inner);
    }

    // Untyped provider error — surface only the status code, never the raw body.
    private PaymentFailedException FailRaw(string op, RawError raw, Exception inner)
    {
        _logger?.LogWarning("PayPal {Op} failed with HTTP {Status}.", op, (int)raw.StatusCode);
        return new PaymentFailedException($"PayPal {op} failed with HTTP status {(int)raw.StatusCode}.", inner);
    }

    // A JsonException here is a deterministic rejection whose detail was lost while the typed error was being
    // built (or a 2xx whose body drifted). Either way it is a terminal payment failure — NOT a transient
    // outage to retry — so it maps to PaymentFailedException, not a 5xx.
    private PaymentFailedException FailUnreadable(string op, Exception inner)
    {
        _logger?.LogWarning("PayPal {Op} returned an unreadable response body.", op);
        return new PaymentFailedException($"PayPal {op} returned a response that could not be processed.", inner);
    }

    private PaymentFailedException FailTransport(string op, Exception inner)
    {
        _logger?.LogWarning("PayPal {Op} could not reach the provider.", op);
        return new PaymentFailedException($"PayPal {op} could not reach the payment provider.", inner);
    }

    private PaymentFailedException FailUnknown(string op, Exception inner)
    {
        _logger?.LogWarning("PayPal {Op} failed with an unrecognised error.", op);
        return new PaymentFailedException($"PayPal {op} failed.", inner);
    }
}
