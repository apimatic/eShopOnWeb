using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A requested line of an order: a catalog item and how many units.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>
/// How to pay for an order: exactly one of raw <see cref="Card"/> details (one-off payment) or a
/// <see cref="SavedPaymentMethodId"/> naming one of the shopper's saved cards.
/// </summary>
public record PayOrderCommand(CardDetails? Card, int? SavedPaymentMethodId);

/// <summary>
/// Places, pays for, and refunds orders. Payment and refund are idempotent in effect: a repeated
/// request for the same order never produces a second charge or a second refund.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Creates an order for the buyer from catalog line items. Starts awaiting payment.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, Address shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>Pays for the buyer's order via PayPal using card details or a saved card.</summary>
    Task<Order> PayOrderAsync(string buyerId, int orderId, PayOrderCommand command, CancellationToken cancellationToken = default);

    /// <summary>Refunds the buyer's order in full.</summary>
    Task<Order> RefundOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken = default);

    /// <summary>All of the buyer's orders, most recent first, with their payment state.</summary>
    Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);
}
