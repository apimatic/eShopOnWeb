using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A line item requested when placing an order: a catalog item id and how many.</summary>
public sealed record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>How an order is being paid: a one-off raw card, or one of the shopper's saved cards.</summary>
public sealed class PayOrderInstruction
{
    public PayPalCardDetails? Card { get; init; }
    public int? SavedPaymentMethodId { get; init; }
}

/// <summary>
/// Orchestrates the money movement over an order's lifecycle: place → authorize (hold) →
/// fulfil (capture) / cancel (void) → refund. Enforces buyer ownership and idempotency.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Places an order for the buyer from catalog items; it starts awaiting payment.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines,
        Address shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>Authorizes (holds) the order total. Idempotent: a re-submit returns the existing hold.</summary>
    Task<Order> PayAsync(int orderId, string buyerId, PayOrderInstruction instruction,
        CancellationToken cancellationToken = default);

    /// <summary>Operator action: fulfils the order and captures the held funds, renewing a stale hold first.</summary>
    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: cancels an unfulfilled order and releases the held funds.</summary>
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a fulfilled order, fully or partially. Idempotent on the caller's key.</summary>
    Task<Refund> RefundAsync(int orderId, string buyerId, string idempotencyKey, decimal? amount,
        CancellationToken cancellationToken = default);

    /// <summary>The buyer's own orders with their payment state.</summary>
    Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>A single order, only if it belongs to the buyer.</summary>
    Task<Order> GetOrderForBuyerAsync(int orderId, string buyerId, CancellationToken cancellationToken = default);
}
