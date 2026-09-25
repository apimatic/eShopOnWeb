using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A line requested when placing an order: a catalog item and how many.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>
/// How to fund a payment: either one-off card details, or the id of one of the shopper's saved
/// cards. Exactly one must be supplied.
/// </summary>
public record PayInstruction(PayPalCardDetails? Card, int? SavedPaymentMethodId);

/// <summary>
/// Orchestrates the money movement for an order: place → pay (authorize hold) → fulfil (capture)
/// / cancel (void) / refund. Reuses the existing <see cref="Order"/> aggregate.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Places an order from catalog items for the shopper; starts awaiting payment.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, CancellationToken cancellationToken);

    /// <summary>Authorizes (holds) the order total for the shopper's own order.</summary>
    Task<Order> PayAsync(int orderId, string buyerId, PayInstruction instruction, CancellationToken cancellationToken);

    /// <summary>Operator: fulfils the order, capturing the held funds (renewing a stale hold first).</summary>
    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken);

    /// <summary>Operator: cancels a not-yet-fulfilled order, releasing the held funds.</summary>
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken);

    /// <summary>Refunds the shopper's own fulfilled order, in full or in part, idempotently.</summary>
    Task<OrderRefund> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>The shopper's own orders with their payment state.</summary>
    Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken);

    /// <summary>A single order owned by the shopper, or null.</summary>
    Task<Order?> GetMyOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken);
}
