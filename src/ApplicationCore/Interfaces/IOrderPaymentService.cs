using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A requested catalog line for a new order: which item and how many.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>
/// Coordinates the money movement over an <see cref="Order"/>: placing it awaiting payment,
/// authorizing (holding) funds, capturing at fulfilment, cancelling (releasing) and refunding.
/// Amounts always come from catalog prices; the currency comes from configuration.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Places an order from catalog lines for the shopper; it starts awaiting payment.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines,
        Address? shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Authorizes (holds) the order total, paying either with the supplied card or with one of the
    /// shopper's saved cards. Idempotent: a double-click never authorizes twice.
    /// </summary>
    Task<Order> AuthorizeOrderAsync(string buyerId, int orderId, CardDetails? card,
        int? savedPaymentMethodId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fulfils the order and captures the held funds. Renews a stale authorization rather than
    /// failing outright; if it can no longer be renewed, reports so in operator-actionable terms.
    /// </summary>
    Task<Order> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Cancels before fulfilment: voids the authorization so the held funds are released.</summary>
    Task<Order> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refunds a captured order (the caller's own) in full or in part under a caller-supplied
    /// idempotency key. Repeating the same key returns the same refund; the total refunded never
    /// exceeds the capture.
    /// </summary>
    Task<(Order Order, PaymentRefund Refund)> RefundOrderAsync(string buyerId, int orderId, decimal? amount,
        string idempotencyKey, CancellationToken cancellationToken = default);
}
