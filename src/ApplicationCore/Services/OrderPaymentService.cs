using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private const string Currency = "USD";

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPaymentGateway _paymentGateway;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<Buyer> buyerRepository,
        IPaymentGateway paymentGateway)
    {
        _orderRepository = orderRepository;
        _buyerRepository = buyerRepository;
        _paymentGateway = paymentGateway;
    }

    public async Task<Order> PayWithCardAsync(string buyerId, int orderId, CardDetails card, CancellationToken cancellationToken = default)
    {
        var order = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);

        if (order.PaymentStatus == OrderPaymentStatus.Paid)
        {
            return order; // idempotent: never charge twice for the same order
        }

        EnsurePayable(order);

        var request = new CardPaymentRequest(order.Id.ToString(), order.Total(), Currency, card, PayKey(order));
        var result = await _paymentGateway.ChargeCardAsync(request, cancellationToken);

        order.MarkAsPaid(result.PayPalOrderId, result.CaptureId);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> PayWithSavedCardAsync(string buyerId, int orderId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);

        if (order.PaymentStatus == OrderPaymentStatus.Paid)
        {
            return order; // idempotent
        }

        EnsurePayable(order);

        // Look the card up through the buyer aggregate — this is what stops one shopper paying with another's card.
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), cancellationToken);
        var paymentMethod = buyer?.GetPaymentMethod(paymentMethodId);
        if (paymentMethod is null || string.IsNullOrEmpty(paymentMethod.PayPalTokenId))
        {
            throw new SavedCardNotFoundException(paymentMethodId);
        }

        var request = new SavedCardPaymentRequest(order.Id.ToString(), order.Total(), Currency, paymentMethod.PayPalTokenId!, PayKey(order));
        var result = await _paymentGateway.ChargeSavedCardAsync(request, cancellationToken);

        order.MarkAsPaid(result.PayPalOrderId, result.CaptureId);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> RefundAsync(string buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);

        if (order.PaymentStatus == OrderPaymentStatus.Refunded)
        {
            return order; // idempotent: never refund twice
        }

        if (order.PaymentStatus != OrderPaymentStatus.Paid || string.IsNullOrEmpty(order.PayPalCaptureId))
        {
            throw new PaymentStateException($"Order {orderId} cannot be refunded because it has not been paid.");
        }

        var result = await _paymentGateway.RefundAsync(new RefundPaymentRequest(order.PayPalCaptureId!, RefundKey(order)), cancellationToken);

        order.MarkAsRefunded(result.RefundId);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<IReadOnlyList<Order>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
    }

    private async Task<Order> LoadOwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || order.BuyerId != buyerId)
        {
            throw new OrderNotFoundException(orderId);
        }

        return order;
    }

    private static void EnsurePayable(Order order)
    {
        if (order.PaymentStatus == OrderPaymentStatus.Refunded)
        {
            throw new PaymentStateException($"Order {order.Id} has been refunded and cannot be paid.");
        }
    }

    // Idempotency keys are stable per order (so a double-click sends PayPal the same request id and
    // cannot produce a second charge/refund) yet unique per order instance. The order's immutable
    // creation timestamp keeps the key unique even when a fresh in-memory database restarts ids at 1,
    // which would otherwise collide with a prior run's still-remembered PayPal request id.
    private static string PayKey(Order order) => $"order-{order.Id}-{order.OrderDate.UtcTicks}-pay";

    private static string RefundKey(Order order) => $"order-{order.Id}-{order.OrderDate.UtcTicks}-refund";
}
