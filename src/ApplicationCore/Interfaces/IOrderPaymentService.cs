using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A requested order line: how many of a given catalog item to buy.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>
/// Places orders and drives their PayPal payment lifecycle (pay / refund). All operations are
/// scoped to a shopper (<c>buyerId</c>, the JWT subject) and are idempotent in effect.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>
    /// Places a new order for the shopper from catalog items, priced from the catalog in USD.
    /// The order starts <see cref="OrderPaymentStatus.AwaitingPayment"/>.
    /// </summary>
    Task<Order> CreateOrderAsync(
        string buyerId,
        IReadOnlyCollection<OrderLineRequest> lines,
        Address shipToAddress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pays for the shopper's order with PayPal, using either raw card details or one of the
    /// shopper's saved cards (identified by <paramref name="savedPaymentMethodId"/>). Idempotent:
    /// paying an already-paid order returns the existing result without charging again.
    /// </summary>
    Task<Order> PayOrderAsync(
        string buyerId,
        int orderId,
        CardDetails? card,
        int? savedPaymentMethodId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fully refunds the shopper's paid order. Idempotent: refunding an already-refunded order
    /// returns the existing result without refunding again.
    /// </summary>
    Task<Order> RefundOrderAsync(
        string buyerId,
        int orderId,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the shopper's orders (with items) and their payment state, newest first.</summary>
    Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(
        string buyerId,
        CancellationToken cancellationToken = default);

    /// <summary>Returns one of the shopper's orders by id, or null if it isn't theirs.</summary>
    Task<Order?> GetOrderForBuyerAsync(
        string buyerId,
        int orderId,
        CancellationToken cancellationToken = default);
}
