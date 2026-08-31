using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderPaymentService
{
    /// <summary>Place an order from catalog items; it starts awaiting payment.</summary>
    Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> items, CancellationToken cancellationToken = default);

    /// <summary>
    /// Authorize (hold) the order total, either with one-off card details or with one of the
    /// shopper's saved cards. Idempotent: paying an already-authorized order returns its payment.
    /// </summary>
    Task<Payment> PayAsync(string buyerId, int orderId, PayOrderCommand command, CancellationToken cancellationToken = default);

    /// <summary>Fulfil the order and capture the held funds, renewing a stale authorization if needed.</summary>
    Task<Payment> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Cancel before fulfilment, releasing the shopper's held funds.</summary>
    Task<Payment?> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refund a fulfilled order, in full (amount null) or in part. Repeating the same
    /// idempotency key returns the original refund instead of refunding twice.
    /// </summary>
    Task<PaymentRefund> RefundAsync(int orderId, decimal? amount, string idempotencyKey, string? noteToPayer, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> ListOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);
}

public class OrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

/// <summary>Exactly one of Card or SavedPaymentMethodId must be set.</summary>
public class PayOrderCommand
{
    public GatewayCardDetails? Card { get; set; }
    public int? SavedPaymentMethodId { get; set; }
}
