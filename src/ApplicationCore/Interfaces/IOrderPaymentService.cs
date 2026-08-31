using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderPaymentService
{
    /// <summary>Place an order from catalog items at current catalog prices. Starts AwaitingPayment.</summary>
    Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderItemRequest> items, Address shipToAddress, CancellationToken ct = default);

    /// <summary>
    /// Authorize (hold) the order total with a raw card or a saved card.
    /// Idempotent: repeating the call for an already-authorized order returns the existing payment.
    /// </summary>
    Task<Payment> AuthorizeAsync(string buyerId, int orderId, GatewayCardDetails? card, int? savedCardId, CancellationToken ct = default);

    /// <summary>Fulfil the order: capture the held funds. Renews a stale authorization when possible.</summary>
    Task<Payment> FulfilAsync(int orderId, CancellationToken ct = default);

    /// <summary>Cancel before fulfilment: release the held funds.</summary>
    Task<Order> CancelAsync(int orderId, CancellationToken ct = default);

    /// <summary>
    /// Refund after fulfilment, in full (amount null) or in part. The idempotency key is
    /// caller-supplied: repeating under the same key returns the original refund.
    /// </summary>
    Task<PaymentRefund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct = default);

    /// <summary>The caller's own orders with their payment state.</summary>
    Task<IReadOnlyList<Order>> ListOrdersAsync(string buyerId, CancellationToken ct = default);
}

public record OrderItemRequest(int CatalogItemId, int Quantity);
