using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record OrderItemRequest(int CatalogItemId, int Quantity);

/// <summary>Orchestrates the pay-for-an-order flow: place, authorize (pay), fulfil, cancel, refund.
/// Talks to PayPal through <see cref="IPayPalGateway"/> and persists state through the Order
/// aggregate. Every mutating method is idempotent in effect for retried calls.</summary>
public interface IOrderPaymentService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderItemRequest> items, Address shipToAddress);

    /// <summary>Authorizes the order total with PayPal. Supply either <paramref name="card"/> for a
    /// one-off card payment or <paramref name="paymentMethodId"/> to pay with a saved card.</summary>
    Task<Order> AuthorizePaymentAsync(string buyerId, int orderId, PayPalCardDetails? card, int? paymentMethodId);

    /// <summary>Operator action: captures the held funds, renewing a stale authorization first if needed.</summary>
    Task<Order> FulfilOrderAsync(int orderId);

    /// <summary>Operator action: cancels before fulfilment, releasing any held funds.</summary>
    Task<Order> CancelOrderAsync(int orderId);

    /// <summary>Shopper action, scoped to their own order: refunds the captured payment, in full
    /// (amount omitted) or in part.</summary>
    Task<Refund> RefundOrderAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey);

    Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId);
}
