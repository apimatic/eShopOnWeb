using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the money movement over an order's life: place → authorize (hold) → fulfil
/// (capture) → cancel (void) / refund. Buyer-scoped actions take the caller's id and act only on
/// that shopper's orders; operator actions (fulfil, cancel) act on any order.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Place an order from catalog lines, in the awaiting-payment state.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, Address? shipToAddress,
        CancellationToken ct);

    /// <summary>
    /// Authorize (hold) the order total. Pays with a one-off <paramref name="card"/> or one of the
    /// shopper's saved cards (<paramref name="savedPaymentMethodId"/>). Idempotent per order.
    /// </summary>
    Task<Order> AuthorizeAsync(int orderId, string buyerId, CardDetails? card, int? savedPaymentMethodId,
        CancellationToken ct);

    /// <summary>Operator: fulfil the order and capture the money, renewing a stale hold if needed.</summary>
    Task<Order> FulfilAsync(int orderId, CancellationToken ct);

    /// <summary>Operator: cancel before fulfilment, releasing the held funds.</summary>
    Task<Order> CancelAsync(int orderId, CancellationToken ct);

    /// <summary>Refund a captured order, full or partial, de-duplicated on the caller's idempotency key.</summary>
    Task<PaymentRefund> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey,
        CancellationToken ct);

    /// <summary>The shopper's own orders, with payment state.</summary>
    Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken ct);
}

/// <summary>One line of a placed order: a catalog item and a quantity.</summary>
public record OrderLine(int CatalogItemId, int Quantity);
