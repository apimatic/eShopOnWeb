using System;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IReadRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IPaymentGateway _gateway;
    private readonly PayPalSettings _settings;

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IReadRepository<SavedPaymentMethod> savedCardRepository,
        IPaymentGateway gateway,
        PayPalSettings settings)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _gateway = gateway;
        _settings = settings;
    }

    public async Task<Payment> AuthorizeOrderAsync(int orderId, string buyerId, CardDetails? card,
        int? savedPaymentMethodId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (card is null && savedPaymentMethodId is null)
        {
            throw new PaymentException("Provide either card details or a saved card to pay with.");
        }
        if (card is not null && savedPaymentMethodId is not null)
        {
            throw new PaymentException("Provide either card details or a saved card, not both.");
        }

        // Load with items so the total (the amount to hold) is computed from the order lines.
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || order.BuyerId != buyerId)
        {
            throw new PaymentNotFoundException($"Order {orderId} was not found.");
        }

        // Idempotent: if the order already has a payment, never authorize a second time.
        var existing = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new PaymentException($"Order {orderId} is '{order.Status}' and cannot be paid.");
        }

        var amount = order.Total();
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        var currency = _settings.Currency;
        var idempotencyKey = $"order-{orderId}-authorize";

        AuthorizationResult authorization;
        int? usedSavedCardId = null;

        if (savedPaymentMethodId is not null)
        {
            var savedCard = await _savedCardRepository.GetByIdAsync(savedPaymentMethodId.Value, cancellationToken);
            if (savedCard is null || savedCard.BuyerId != buyerId)
            {
                throw new PaymentNotFoundException($"Saved card {savedPaymentMethodId} was not found.");
            }
            usedSavedCardId = savedCard.Id;
            authorization = await _gateway.AuthorizeWithVaultedCardAsync(amount, currency, savedCard.VaultId, idempotencyKey, cancellationToken);
        }
        else
        {
            authorization = await _gateway.AuthorizeWithCardAsync(amount, currency, card!, idempotencyKey, cancellationToken);
        }

        var payment = new Payment(
            orderId,
            buyerId,
            currency,
            amount,
            authorization.PayPalOrderId,
            authorization.AuthorizationId,
            authorization.Status,
            authorization.ExpiresAt,
            usedSavedCardId);

        await _paymentRepository.AddAsync(payment, cancellationToken);

        order.MarkPaymentAuthorized();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        return payment;
    }

    public async Task<Payment> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            throw new PaymentNotFoundException($"Order {orderId} was not found.");
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken);
        if (payment is null)
        {
            throw new PaymentException($"Order {orderId} has no authorized payment to capture.");
        }

        // Idempotent: already captured.
        if (payment.Status == PaymentStatus.Captured ||
            payment.Status == PaymentStatus.PartiallyRefunded ||
            payment.Status == PaymentStatus.Refunded)
        {
            return payment;
        }

        if (order.Status != OrderStatus.PaymentAuthorized || payment.Status != PaymentStatus.Authorized)
        {
            throw new PaymentException($"Order {orderId} is '{order.Status}' and cannot be fulfilled.");
        }

        var captureKey = $"order-{orderId}-capture";
        var isStale = payment.AuthorizationExpiresAt is { } expires && expires <= DateTimeOffset.UtcNow;

        // Renew a hold that has gone stale before attempting the capture.
        if (isStale)
        {
            await RenewAuthorizationAsync(order, payment, cancellationToken);
        }

        CaptureResult capture;
        try
        {
            capture = await _gateway.CaptureAsync(payment.AuthorizationId, captureKey, cancellationToken);
        }
        catch (PaymentException) when (!isStale)
        {
            // The hold may have expired between authorization and fulfilment even though its recorded
            // expiry had not passed. Try to renew it once, then capture again.
            await RenewAuthorizationAsync(order, payment, cancellationToken);
            capture = await _gateway.CaptureAsync(payment.AuthorizationId, captureKey, cancellationToken);
        }

        payment.MarkCaptured(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        return payment;
    }

    private async Task RenewAuthorizationAsync(Order order, Payment payment, CancellationToken cancellationToken)
    {
        try
        {
            var renewed = await _gateway.ReauthorizeAsync(payment.AuthorizationId, payment.Amount, payment.Currency, cancellationToken);
            payment.RenewAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }
        catch (PaymentException ex)
        {
            throw new PaymentException(
                $"The authorization for order {order.Id} has expired and can no longer be renewed. " +
                "Ask the shopper to place and pay for the order again.", ex);
        }
    }

    public async Task<Payment?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            throw new PaymentNotFoundException($"Order {orderId} was not found.");
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken);

        // Idempotent: already cancelled.
        if (order.Status == OrderStatus.Cancelled)
        {
            return payment;
        }

        if (order.Status != OrderStatus.AwaitingPayment && order.Status != OrderStatus.PaymentAuthorized)
        {
            throw new PaymentException($"Order {orderId} is '{order.Status}' and cannot be cancelled. A fulfilled order can only be refunded.");
        }

        if (payment is not null && payment.Status == PaymentStatus.Authorized)
        {
            await _gateway.VoidAsync(payment.AuthorizationId, $"order-{orderId}-void", cancellationToken);
            payment.MarkVoided();
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        return payment;
    }

    public async Task<Payment> RefundOrderAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null || order.BuyerId != buyerId)
        {
            throw new PaymentNotFoundException($"Order {orderId} was not found.");
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken);
        if (payment is null || payment.CaptureId is null ||
            (payment.Status != PaymentStatus.Captured && payment.Status != PaymentStatus.PartiallyRefunded))
        {
            throw new PaymentException($"Order {orderId} has no captured payment to refund.");
        }

        // Idempotent: a repeat under the same key returns the existing payment/refund unchanged.
        if (payment.FindRefundByIdempotencyKey(idempotencyKey) is not null)
        {
            return payment;
        }

        var remaining = payment.RefundableRemaining();
        var refundAmount = amount ?? remaining;

        if (refundAmount <= 0m)
        {
            throw new PaymentException($"Order {orderId} has nothing left to refund.");
        }
        if (refundAmount > remaining)
        {
            throw new PaymentException(
                $"Refund of {refundAmount:0.00} exceeds the {remaining:0.00} still refundable on order {orderId}.");
        }

        var result = await _gateway.RefundAsync(payment.CaptureId, refundAmount, payment.Currency, idempotencyKey, cancellationToken);

        payment.AddRefund(result.RefundId, refundAmount, result.Status, idempotencyKey);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        order.MarkRefunded(fully: payment.Status == PaymentStatus.Refunded);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        return payment;
    }
}
