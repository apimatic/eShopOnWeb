using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A requested line on a new order: a catalog item and how many of it.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>
/// Orchestrates the additive pay-for-an-order flow: placing an order awaiting payment, authorizing
/// (holding) the money, fulfilling (capturing) it, cancelling (releasing the hold) and refunding.
/// Shopper-scoped operations verify the caller owns the order; operator operations do not.
/// </summary>
public interface IPaymentOrderService
{
    /// <summary>Places a new order from catalog items for a shopper. The order starts awaiting payment.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines, Address? shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Authorizes (holds) the order total for the shopper, using either raw card details or one of the
    /// shopper's saved cards. Idempotent in effect: a double-click never authorizes twice.
    /// </summary>
    Task<Order> AuthorizeAsync(string buyerId, int orderId, CardDetails? card, int? paymentMethodId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: marks the order fulfilled and captures the held money (renewing a stale hold if needed).</summary>
    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: cancels the order before fulfilment, releasing the held funds.</summary>
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refunds the captured payment for the shopper's order, fully or partially, keyed for idempotency.</summary>
    Task<PaymentRefund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Returns all of the shopper's orders with their payment state.</summary>
    Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Loads one order (with payment) scoped to its owner; null if missing or not the caller's.</summary>
    Task<Order?> GetOrderForBuyerAsync(string buyerId, int orderId, CancellationToken cancellationToken = default);
}
