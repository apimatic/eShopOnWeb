using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A catalog item id and the quantity ordered of it.</summary>
public readonly record struct OrderLine(int CatalogItemId, int Quantity);

/// <summary>
/// Orchestrates the pay-for-an-order flow on top of the existing Order/OrderItem model and the
/// PayPal gateway: place → authorize (hold) → fulfil (capture) → cancel (void) / refund.
/// Shopper-scoped operations take the caller's buyer id and act only on that caller's orders;
/// operator operations (fulfil, cancel) act on any order.
/// </summary>
public interface IOrderPaymentService
{
    Task<Order> PlaceOrderAsync(string buyerId, IEnumerable<OrderLine> lines, Address shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>Authorize the order total with a one-off card or one of the shopper's saved cards.</summary>
    Task<Order> AuthorizeAsync(int orderId, string buyerId, CardDetails? card, int? savedPaymentMethodId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: capture the held funds. Renews a stale authorization first if needed.</summary>
    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: release the hold before fulfilment.</summary>
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Shopper action: refund the caller's own captured order, in full or in part, under an idempotency key.
    /// Returns the order with the (new or replayed) refund attached; find it via
    /// <see cref="Order.FindRefundByIdempotencyKey"/>.
    /// </summary>
    Task<Order> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);
}

/// <summary>Orchestrates the saved-card flow on top of the PayPal Vault.</summary>
public interface IPaymentMethodService
{
    Task<Entities.BuyerAggregate.SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Entities.BuyerAggregate.SavedPaymentMethod>> GetCardsForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);
    Task DeleteCardAsync(int paymentMethodId, string buyerId, CancellationToken cancellationToken = default);
}

/// <summary>Builds the reconciliation report that lines PayPal's transactions up against eShop orders.</summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
