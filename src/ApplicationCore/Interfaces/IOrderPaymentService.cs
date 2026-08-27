using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class OrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

/// <summary>An order together with the current state of its payment (if payment was started).</summary>
public class OrderPaymentState
{
    public OrderPaymentState(Order order, OrderPayment? payment)
    {
        Order = order;
        Payment = payment;
    }

    public Order Order { get; }
    public OrderPayment? Payment { get; }
}

public interface IOrderPaymentService
{
    Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderItemRequest> items,
        Address? shippingAddress, CancellationToken cancellationToken = default);

    /// <summary>Authorizes (holds) the order total. Returns null when the order does not belong to the buyer.</summary>
    Task<OrderPaymentState?> PayOrderAsync(string buyerId, int orderId, CardDetails? card,
        int? savedCardId, CancellationToken cancellationToken = default);

    /// <summary>Captures the held funds (operator). Renews a stale authorization when possible.</summary>
    Task<OrderPaymentState?> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Cancels before fulfilment, releasing any held funds (operator).</summary>
    Task<OrderPaymentState?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refunds the captured payment in full (amount null) or in part. Idempotent per idempotencyKey.</summary>
    Task<PaymentRefund?> RefundOrderAsync(string buyerId, int orderId, decimal? amount,
        string idempotencyKey, string? noteToPayer, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderPaymentState>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);
}
