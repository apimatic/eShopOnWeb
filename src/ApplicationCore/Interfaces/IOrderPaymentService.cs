using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed record OrderItemRequest(int CatalogItemId, int Quantity);

/// <summary>
/// Orchestrates the money movement for orders: authorize (pay), capture
/// (fulfil), void (cancel) and refund, keeping the eShop order and the
/// PayPal-owned payment state in sync.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Places an order from catalog items at current catalog prices. The order starts awaiting payment.</summary>
    Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderItemRequest> items, Address shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Authorizes (holds) the order total with PayPal, using either one-off card
    /// details or one of the shopper's saved cards. Idempotent: paying an
    /// already-paid order returns the existing authorization.
    /// </summary>
    Task<Payment> PayOrderAsync(int orderId, string buyerId, CardDetails? card, int? savedPaymentMethodId, string currency, CancellationToken cancellationToken = default);

    /// <summary>
    /// Captures the authorization when the operator fulfils the order. Renews a
    /// stale authorization first; throws
    /// <see cref="Exceptions.AuthorizationNotRenewableException"/> when it can no
    /// longer be renewed. Idempotent.
    /// </summary>
    Task<Payment> FulfilOrderAsync(int orderId, string currency, CancellationToken cancellationToken = default);

    /// <summary>Cancels an order before fulfilment, releasing the shopper's held funds. Idempotent.</summary>
    Task<Order> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refunds a fulfilled order, fully (amount null) or partially, never beyond
    /// what was captured. Repeating the same idempotency key returns the original
    /// refund instead of refunding twice.
    /// </summary>
    Task<PaymentRefund> RefundOrderAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey, string? noteToPayer, string currency, CancellationToken cancellationToken = default);
}
