using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the order payment lifecycle: place order, authorize (hold), capture at
/// fulfilment, void on cancel, refund after fulfilment. All operations are idempotent
/// in effect.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Places an order from catalog items for the given shopper; starts AwaitingPayment.</summary>
    Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderItemRequest> items, Address? shippingAddress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Authorizes the order total with a one-off card or a saved card. Repeating the call
    /// for an already-authorized order returns the existing authorization.
    /// </summary>
    Task<OrderPayment> PayOrderAsync(string buyerId, int orderId, PaymentSourceSelection source,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator: fulfils the order and captures the money. Renews a stale authorization
    /// when possible; throws AuthorizationNotRenewableException when it is not.
    /// </summary>
    Task<OrderPayment> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator: cancels the order before fulfilment, releasing the shopper's held funds.</summary>
    Task<OrderPayment?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator: refunds the captured payment in full (amount null) or in part.
    /// Repeating the call under the same idempotency key returns the original refund.
    /// </summary>
    Task<RefundResult> RefundOrderAsync(int orderId, decimal? amount, string idempotencyKey, string? note,
        CancellationToken cancellationToken = default);

    /// <summary>The caller's orders with their payment state.</summary>
    Task<IReadOnlyList<OrderWithPayment>> ListOrdersWithPaymentsAsync(string buyerId, CancellationToken cancellationToken = default);
}

public sealed record OrderItemRequest(int CatalogItemId, int Quantity);

public sealed record OrderWithPayment(Order Order, OrderPayment? Payment);

public sealed record RefundResult(PaymentRefund Refund, OrderPayment Payment);
