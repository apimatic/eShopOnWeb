using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    // Shared across requests so concurrent operations on the same order are serialised.
    private static readonly KeyedAsyncLock OrderLocks = new();

    private readonly IRepository<Order> _orderRepository;
    private readonly IReadRepository<Buyer> _buyerRepository;
    private readonly IPayPalGateway _payPalGateway;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Order> orderRepository,
        IReadRepository<Buyer> buyerRepository,
        IPayPalGateway payPalGateway,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _buyerRepository = buyerRepository;
        _payPalGateway = payPalGateway;
        _logger = logger;
    }

    public async Task<Order> PayOrderAsync(int orderId, string buyerId, PaymentInstruction instruction, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(instruction, nameof(instruction));

        using (await OrderLocks.LockAsync(OrderKey(orderId), cancellationToken))
        {
            var order = await LoadOwnedOrderAsync(orderId, buyerId);

            // Idempotency: a repeated pay for an already-paid order returns the same result
            // without charging again.
            if (order.PaymentStatus == OrderPaymentStatus.Paid)
            {
                _logger.LogInformation($"Order {orderId} is already paid (capture {order.PaymentCaptureId}); returning existing payment.");
                return order;
            }

            if (order.PaymentStatus == OrderPaymentStatus.Refunded)
            {
                throw new PaymentException($"Order {orderId} has been refunded and cannot be paid.");
            }

            var amount = new Money(order.Total(), "USD");
            // Deterministic key so a double-submit maps to a single PayPal order/capture.
            var idempotencyKey = $"eshop-pay-order-{orderId}";

            PayPalChargeResult charge;
            if (instruction.SavedPaymentMethodId.HasValue)
            {
                var vaultToken = await ResolveSavedCardTokenAsync(buyerId, instruction.SavedPaymentMethodId.Value);
                charge = await _payPalGateway.ChargeWithVaultedCardAsync(amount, vaultToken, idempotencyKey, cancellationToken);
            }
            else if (instruction.Card is not null)
            {
                charge = await _payPalGateway.ChargeWithCardAsync(amount, instruction.Card, idempotencyKey, cancellationToken);
            }
            else
            {
                throw new PaymentException("No payment instrument was supplied: provide card details or a saved card id.");
            }

            order.MarkPaid(charge.PayPalOrderId, charge.CaptureId);
            await _orderRepository.UpdateAsync(order);

            _logger.LogInformation($"Order {orderId} paid via PayPal order {charge.PayPalOrderId}, capture {charge.CaptureId} ({charge.CaptureStatus}).");
            return order;
        }
    }

    public async Task<Order> RefundOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        using (await OrderLocks.LockAsync(OrderKey(orderId), cancellationToken))
        {
            var order = await LoadOwnedOrderAsync(orderId, buyerId);

            // Idempotency: a repeated refund of an already-refunded order returns the same result.
            if (order.PaymentStatus == OrderPaymentStatus.Refunded)
            {
                _logger.LogInformation($"Order {orderId} is already refunded (refund {order.PaymentRefundId}); returning existing refund.");
                return order;
            }

            if (order.PaymentStatus != OrderPaymentStatus.Paid || string.IsNullOrEmpty(order.PaymentCaptureId))
            {
                throw new PaymentException($"Order {orderId} is not paid and cannot be refunded.");
            }

            var idempotencyKey = $"eshop-refund-order-{orderId}";
            var refund = await _payPalGateway.RefundCaptureAsync(order.PaymentCaptureId!, idempotencyKey, cancellationToken);

            order.MarkRefunded(refund.RefundId);
            await _orderRepository.UpdateAsync(order);

            _logger.LogInformation($"Order {orderId} refunded via PayPal refund {refund.RefundId} ({refund.RefundStatus}).");
            return order;
        }
    }

    private async Task<Order> LoadOwnedOrderAsync(int orderId, string buyerId)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId));

        // Treat a not-owned order exactly like a missing one so shoppers cannot probe each other's orders.
        if (order is null || order.BuyerId != buyerId)
        {
            throw new OrderNotFoundException(orderId);
        }

        return order;
    }

    private async Task<string> ResolveSavedCardTokenAsync(string buyerId, int paymentMethodId)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId));
        PaymentMethod? paymentMethod = null;
        if (buyer is not null)
        {
            foreach (var pm in buyer.PaymentMethods)
            {
                if (pm.Id == paymentMethodId) { paymentMethod = pm; break; }
            }
        }

        if (paymentMethod is null || string.IsNullOrEmpty(paymentMethod.CardId))
        {
            throw new PaymentException($"Saved card {paymentMethodId} was not found for this shopper.");
        }

        return paymentMethod.CardId!;
    }

    private static string OrderKey(int orderId) => $"order:{orderId}";
}
