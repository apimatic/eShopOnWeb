using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class OrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public interface IOrderPaymentService
{
    /// <summary>Places an order from catalog items; the order starts awaiting payment.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderItemRequest> items, Address? shippingAddress);

    /// <summary>
    /// Authorizes (holds) the order total, either with one-off card details or with one of
    /// the buyer's saved cards. Idempotent: repeating the call returns the existing hold.
    /// </summary>
    Task<Payment> PayOrderAsync(string buyerId, int orderId, GatewayCardDetails? card, int? savedCardId);

    /// <summary>Captures the held funds. Renews a stale authorization when possible.</summary>
    Task<Payment> FulfilOrderAsync(int orderId);

    /// <summary>Cancels before fulfilment, releasing the shopper's held funds.</summary>
    Task<Payment?> CancelOrderAsync(int orderId);

    /// <summary>
    /// Refunds a captured payment, in full (amount null) or in part. Repeating the call with
    /// the same idempotency key returns the original refund instead of refunding twice.
    /// Shopper-scoped: the order must belong to the caller.
    /// </summary>
    Task<PaymentRefund> RefundOrderAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, string? noteToPayer);

    Task<IReadOnlyList<Order>> GetBuyerOrdersAsync(string buyerId);

    Task<IReadOnlyList<Payment>> GetPaymentsForOrdersAsync(IEnumerable<int> orderIds);

    Task<Payment?> GetPaymentForOrderAsync(int orderId);
}
