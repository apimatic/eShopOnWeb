using System;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private const string Currency = "USD";

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPaymentGateway _paymentGateway;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IPaymentGateway paymentGateway)
    {
        _orderRepository = orderRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _paymentGateway = paymentGateway;
    }

    public async Task<Order?> PayAsync(string buyerId, int orderId, CardDetails? card, int? savedPaymentMethodId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        if (card is null && savedPaymentMethodId is null)
            throw new ArgumentException("Provide either card details or a saved payment method to pay with.");
        if (card is not null && savedPaymentMethodId is not null)
            throw new ArgumentException("Provide either card details or a saved payment method, not both.");

        var order = await GetOwnedOrderAsync(buyerId, orderId, cancellationToken);
        if (order is null) return null;

        // Idempotent: an already-paid order is returned unchanged so a double-click never re-charges.
        if (order.PaymentStatus == PaymentStatus.Paid) return order;
        if (order.PaymentStatus == PaymentStatus.Refunded)
            throw new InvalidOperationException("A refunded order cannot be paid.");

        var amount = order.Total();

        PaymentResult result;
        if (savedPaymentMethodId is not null)
        {
            var method = await _paymentMethodRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByBuyerAndIdSpecification(buyerId, savedPaymentMethodId.Value), cancellationToken);
            if (method is null) throw new PaymentMethodNotFoundException(savedPaymentMethodId.Value);

            result = await _paymentGateway.ChargeVaultedCardAsync(
                amount, Currency, method.VaultId,
                IdempotencyKeys.PayOrderWithSavedCard(orderId, savedPaymentMethodId.Value), cancellationToken);
        }
        else
        {
            result = await _paymentGateway.ChargeCardAsync(
                amount, Currency, card!,
                IdempotencyKeys.PayOrderWithCard(orderId, card!), cancellationToken);
        }

        order.MarkAsPaid(result.GatewayOrderId, result.CaptureId);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order?> RefundAsync(string buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var order = await GetOwnedOrderAsync(buyerId, orderId, cancellationToken);
        if (order is null) return null;

        // Idempotent: an already-refunded order is returned unchanged so a double-click never re-refunds.
        if (order.PaymentStatus == PaymentStatus.Refunded) return order;
        if (order.PaymentStatus != PaymentStatus.Paid)
            throw new InvalidOperationException("Only a paid order can be refunded.");

        var result = await _paymentGateway.RefundAsync(
            order.PaymentCaptureId!, IdempotencyKeys.RefundOrder(orderId), cancellationToken);

        order.MarkAsRefunded(result.RefundId);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    private async Task<Order?> GetOwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        // Not found, or owned by a different shopper: treat identically so ownership is not leaked.
        if (order is null || order.BuyerId != buyerId) return null;
        return order;
    }
}
