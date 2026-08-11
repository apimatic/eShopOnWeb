using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A requested order line: how many of a catalog item to buy.</summary>
public sealed record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>
/// Orchestrates the money movement for an order across its lifecycle — placing it awaiting payment,
/// authorizing (holding), fulfilling (capturing), cancelling (voiding) and refunding — keeping the
/// eShop order state and the PayPal-owned state in step. Each action is separately invocable and
/// idempotent in effect.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Places an order for the shopper from catalog lines. Starts awaiting payment.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines,
        Address shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Authorizes (holds) the order total with PayPal, using either supplied card details or one of
    /// the shopper's saved cards. Idempotent: a double-click never authorizes twice.
    /// </summary>
    Task<Order> AuthorizeAsync(string buyerId, int orderId, CardDetails? card, int? savedPaymentMethodId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator action: fulfils the order, capturing the held funds. Renews a stale hold rather than
    /// failing outright; a hold that can no longer be renewed is reported in operator terms.
    /// </summary>
    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: cancels before fulfilment, releasing the held funds.</summary>
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refunds the captured payment for the shopper's order, in full or in part. Repeating the same
    /// idempotency key does not refund twice; distinct partial refunds are allowed up to the captured amount.
    /// </summary>
    Task<(Order Order, PaymentRefund Refund)> RefundAsync(string buyerId, int orderId, decimal? amount,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Returns the shopper's own orders with their payment state.</summary>
    Task<IReadOnlyCollection<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);
}
