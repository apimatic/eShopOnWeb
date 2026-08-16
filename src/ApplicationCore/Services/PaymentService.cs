using System;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Drives the money movements over an order against PayPal: authorize (hold), capture (at fulfilment),
/// void (cancel) and refund. Every operation is idempotent in effect, and state PayPal owns is persisted
/// on the order's <see cref="Payment"/> as it changes so later requests can act on it.
/// </summary>
public class PaymentService : IPaymentService
{
    // PayPal authorization statuses we branch on.
    private const string StatusExpired = "EXPIRED";
    private const string StatusVoided = "VOIDED";
    private const string StatusDenied = "DENIED";

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CustomerPaymentMethod> _cardRepository;
    private readonly IPayPalPaymentGateway _gateway;
    private readonly PayPalSettings _settings;

    public PaymentService(IRepository<Order> orderRepository, IRepository<CustomerPaymentMethod> cardRepository,
        IPayPalPaymentGateway gateway, PayPalSettings settings)
    {
        _orderRepository = orderRepository;
        _cardRepository = cardRepository;
        _gateway = gateway;
        _settings = settings;
    }

    public async Task<Order> AuthorizeAsync(int orderId, string buyerId, PaymentInstruction instruction,
        CancellationToken cancellationToken = default)
    {
        if (!instruction.IsValid)
        {
            throw new PaymentStateException("Provide exactly one of: card details, or a saved card id.");
        }

        var order = await _orderRepository.FirstOrDefaultAsync(
            new OrderWithPaymentByIdSpec(orderId, buyerId), cancellationToken)
            ?? throw new OrderNotFoundException(orderId);

        // Idempotency: a repeat (double-click) never places a second hold.
        if (order.Payment is { IsAuthorized: true })
        {
            return order;
        }

        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new PaymentStateException($"Order {orderId} cannot be paid while it is {order.Status}.");
        }

        var currency = _settings.Currency;
        var payment = order.StartPayment(currency);
        // Persist the payment (and its stable idempotency keys) before calling PayPal, so a retry reuses them.
        await _orderRepository.UpdateAsync(order, cancellationToken);

        PayPalAuthorizationResult result;
        if (instruction.SavedPaymentMethodId is int savedId)
        {
            var saved = await _cardRepository.FirstOrDefaultAsync(
                new CustomerPaymentMethodByIdSpecification(savedId, buyerId), cancellationToken)
                ?? throw new PaymentStateException($"Saved card {savedId} was not found for this shopper.");

            result = await _gateway.AuthorizeWithVaultedCardAsync(order.Total(), currency, saved.VaultId,
                payment.AuthorizeRequestId, cancellationToken);
        }
        else
        {
            result = await _gateway.AuthorizeWithCardAsync(order.Total(), currency, instruction.Card!,
                payment.AuthorizeRequestId, cancellationToken);
        }

        payment.SetAuthorization(result.PayPalOrderId, result.AuthorizationId, result.Status,
            result.ExpiresAt, result.InstrumentDescription);
        order.MarkAuthorized();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(
            new OrderWithPaymentByIdSpec(orderId), cancellationToken)
            ?? throw new OrderNotFoundException(orderId);

        var payment = order.Payment;

        // Idempotency: already fulfilled and captured -> return as-is.
        if (order.Status == OrderStatus.Fulfilled && payment is { IsCaptured: true })
        {
            return order;
        }

        if (!order.CanBeFulfilled || payment is null || !payment.IsAuthorized)
        {
            throw new PaymentStateException(
                $"Order {orderId} cannot be fulfilled while it is {order.Status}; it must be authorized first.");
        }

        // Renew a stale hold rather than failing the fulfilment outright.
        var authorization = await _gateway.GetAuthorizationAsync(payment.AuthorizationId!, cancellationToken);
        if (string.Equals(authorization.Status, StatusExpired, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var renewed = await _gateway.ReauthorizeAsync(payment.AuthorizationId!, payment.Amount,
                    payment.CurrencyCode, cancellationToken);
                payment.RenewAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
                await _orderRepository.UpdateAsync(order, cancellationToken);
            }
            catch (PaymentGatewayException ex)
            {
                throw new AuthorizationNotRenewableException(
                    $"The payment hold for order {orderId} has expired and can no longer be renewed. " +
                    "Ask the shopper to pay the order again to place a fresh hold, then fulfil it.", ex);
            }
        }
        else if (string.Equals(authorization.Status, StatusVoided, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(authorization.Status, StatusDenied, StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentStateException(
                $"The payment hold for order {orderId} is {authorization.Status} and cannot be captured.");
        }

        var capture = await _gateway.CaptureAuthorizationAsync(payment.AuthorizationId!,
            payment.CaptureRequestId, cancellationToken);

        payment.SetCapture(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(
            new OrderWithPaymentByIdSpec(orderId), cancellationToken)
            ?? throw new OrderNotFoundException(orderId);

        if (order.Status == OrderStatus.Cancelled)
        {
            return order; // idempotent
        }

        if (!order.CanBeCancelled)
        {
            throw new PaymentStateException(
                $"Order {orderId} cannot be cancelled while it is {order.Status}; it has already been fulfilled.");
        }

        // Release the held funds, if a hold was placed.
        if (order.Payment is { IsAuthorized: true } payment)
        {
            await _gateway.VoidAuthorizationAsync(payment.AuthorizationId!, cancellationToken);
            payment.VoidAuthorization();
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Refund> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await _orderRepository.FirstOrDefaultAsync(
            new OrderWithPaymentByIdSpec(orderId, buyerId), cancellationToken)
            ?? throw new OrderNotFoundException(orderId);

        var payment = order.Payment;
        if (payment is null || !payment.IsCaptured)
        {
            throw new PaymentStateException($"Order {orderId} has no captured payment to refund.");
        }

        // Idempotency: repeating a request under the same key must not refund twice.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        var refundAmount = amount ?? payment.RefundableRemaining();
        if (refundAmount <= 0m)
        {
            throw new PaymentStateException($"Order {orderId} has nothing left to refund.");
        }
        if (refundAmount > payment.RefundableRemaining())
        {
            throw new PaymentStateException(
                $"Refund of {refundAmount:0.00} exceeds the remaining refundable amount of " +
                $"{payment.RefundableRemaining():0.00} for order {orderId}.");
        }

        // Create the refund first so it carries a globally-unique gateway request id; the caller's key is
        // only used for our local dedup above. We persist nothing until PayPal confirms, so a failed call
        // leaves no phantom refund and a caller retry (same key) is re-attempted safely.
        var refund = payment.AddRefund(idempotencyKey, refundAmount, payment.CurrencyCode);

        var result = await _gateway.RefundCaptureAsync(payment.CaptureId!, refundAmount, payment.CurrencyCode,
            refund.GatewayRequestId, cancellationToken);

        refund.RecordResult(result.RefundId, result.Status);
        order.ApplyRefundOutcome();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return refund;
    }
}
