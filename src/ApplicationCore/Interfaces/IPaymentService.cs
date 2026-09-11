using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the money movement over an order's lifecycle: place, authorize (hold), fulfil
/// (capture), cancel (void) and refund. Each action is separately invocable and idempotent in
/// effect. All shopper-scoped actions verify the order belongs to the caller.
/// </summary>
public interface IPaymentService
{
    /// <summary>Places an order from catalog items for the shopper. Returns the new order id.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines, ShippingAddressRequest? shippingAddress, CancellationToken cancellationToken = default);

    /// <summary>Authorizes (holds) the order total against a card or saved card. Idempotent.</summary>
    Task<Order> PayOrderAsync(string buyerId, int orderId, PaymentInstruction instruction, CancellationToken cancellationToken = default);

    /// <summary>Operator action: fulfils the order and captures the held funds, renewing a stale hold if needed.</summary>
    Task<Order> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: cancels an order before fulfilment, releasing any held funds.</summary>
    Task<Order> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a fulfilled order's capture, fully or partially. Returns the order and refund id.</summary>
    Task<(Order Order, int RefundId)> RefundOrderAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>The caller's orders with their payment state.</summary>
    Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);
}
