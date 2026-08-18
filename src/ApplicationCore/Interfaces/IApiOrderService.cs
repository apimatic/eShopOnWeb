using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places and moves orders on behalf of the PublicApi, reusing the app's existing Order/OrderItem
/// model, and drives the SMS notifications that go with each transition. Order placement is
/// shopper-scoped (the buyer is the caller); dispatch and cancel are operator actions.
/// </summary>
public interface IApiOrderService
{
    /// <summary>
    /// Places an order for <paramref name="buyerId"/> from catalog item ids and quantities, and tells
    /// the shopper it was placed.
    /// </summary>
    Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, CancellationToken cancellationToken);

    /// <summary>Marks an order dispatched and notifies the shopper (queuing a follow-up).</summary>
    Task<OrderTransitionResult> DispatchAsync(int orderId, CancellationToken cancellationToken);

    /// <summary>Marks an order cancelled, notifies the shopper, and calls off any pending follow-up.</summary>
    Task<OrderTransitionResult> CancelAsync(int orderId, CancellationToken cancellationToken);

    /// <summary>The caller's own orders.</summary>
    Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken);

    /// <summary>One order, but only if it belongs to the given buyer (null otherwise — no cross-shopper reads).</summary>
    Task<Order?> GetOrderForBuyerAsync(int orderId, string buyerId, CancellationToken cancellationToken);
}

/// <summary>One requested line of an order: a catalog item and how many of it.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>The outcome of placing an order. On success <see cref="Order"/> is set; otherwise <see cref="Error"/> explains why.</summary>
public record PlaceOrderResult(Order? Order, string? Error)
{
    public bool Succeeded => Order is not null;
}

public enum OrderTransitionOutcome
{
    Succeeded,
    OrderNotFound,
    InvalidState
}

/// <summary>The outcome of a dispatch/cancel transition.</summary>
public record OrderTransitionResult(OrderTransitionOutcome Outcome, Order? Order, string? Error);
