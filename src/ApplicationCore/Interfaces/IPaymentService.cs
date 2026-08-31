using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record OrderItemRequest(int CatalogItemId, int Quantity);

/// <summary>
/// Orchestrates the order payment lifecycle: authorize (hold) at checkout,
/// capture at fulfilment, void on cancel, refund on return. All operations are
/// idempotent in effect.
/// </summary>
public interface IPaymentService
{
    /// <summary>Creates an order from catalog items in state AwaitingPayment.</summary>
    Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderItemRequest> items,
        Address? shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Authorizes the order total with either one-off card details or one of the
    /// shopper's saved cards. Repeating the call for an already-authorized order
    /// returns the existing authorization.
    /// </summary>
    Task<Payment> PayOrderAsync(string buyerId, int orderId, CardDetails? card, int? paymentMethodId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator action: captures the held funds. Renews a stale authorization
    /// first; throws an actionable PaymentException if it cannot be renewed.
    /// </summary>
    Task<Payment> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: releases the hold before fulfilment; no money moves.</summary>
    Task<Payment?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns money after fulfilment, in full (amount == null) or in part.
    /// Idempotent per idempotencyKey.
    /// </summary>
    Task<PaymentRefund> RefundOrderAsync(string buyerId, int orderId, decimal? amount,
        string idempotencyKey, string? noteToPayer, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<Payment?> GetPaymentForOrderAsync(int orderId, CancellationToken cancellationToken = default);
}
