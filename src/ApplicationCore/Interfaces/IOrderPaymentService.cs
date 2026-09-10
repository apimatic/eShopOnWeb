using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>An item a shopper wants to order: a catalog item id and how many.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>
/// Orchestrates the pay-for-an-order flow across the order aggregate and PayPal. Shopper-scoped
/// operations take a <c>buyerId</c> and act only on that shopper's data; operator operations
/// (fulfil, cancel) are unscoped and gated at the API by the administrator role.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Places an order from catalog items for the shopper. Starts <see cref="OrderStatus.AwaitingPayment"/>.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines,
        Address shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>Authorizes (holds) the order total, using either inline card details for a one-off
    /// payment or one of the shopper's saved cards. Idempotent: a second call while already
    /// authorized returns the order unchanged.</summary>
    Task<Order> AuthorizeAsync(string buyerId, int orderId, CardDetails? card, int? savedPaymentMethodId,
        CancellationToken cancellationToken = default);

    /// <summary>Operator: fulfils the order, capturing the held funds (renewing a stale hold first).</summary>
    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator: cancels before fulfilment, releasing any held funds.</summary>
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a fulfilled order in full or in part, deduplicated by idempotency key.</summary>
    Task<PaymentRefund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Loads one order scoped to its owner, or null if it is not that shopper's.</summary>
    Task<Order?> GetOrderForBuyerAsync(string buyerId, int orderId, CancellationToken cancellationToken = default);
}
