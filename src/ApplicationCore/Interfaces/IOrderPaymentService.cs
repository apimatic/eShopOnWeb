using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates an order's money movement against PayPal: authorize (hold), fulfil (capture),
/// cancel (void) and refund. All operations are idempotent in effect.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>
    /// Authorizes the order total (holds the money) using either supplied card details or one of
    /// the shopper's saved cards. Scoped to <paramref name="buyerId"/>. Idempotent per order.
    /// </summary>
    Task<Order> AuthorizeAsync(string buyerId, int orderId, PayPalCardDetails? card, int? savedPaymentMethodId);

    /// <summary>Operator action: captures the held funds and records PayPal's fee/net breakdown.</summary>
    Task<Order> FulfilAsync(int orderId);

    /// <summary>Operator action: voids the authorization before fulfilment so no money moves.</summary>
    Task<Order> CancelAsync(int orderId);

    /// <summary>Refunds a fulfilled order in full or in part. Idempotent per idempotency key.</summary>
    Task<(Order Order, OrderRefund Refund)> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, string? noteToPayer);

    /// <summary>The caller's orders with their payment state.</summary>
    Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId);

    /// <summary>Loads a single order owned by the caller, or throws NotFound.</summary>
    Task<Order> GetOrderForBuyerAsync(string buyerId, int orderId);
}
