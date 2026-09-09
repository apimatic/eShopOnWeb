using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A catalog item id and how many of it to order.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>Optional ship-to address supplied when placing an order.</summary>
public record ShipToAddressInput(string Street, string City, string State, string Country, string ZipCode);

/// <summary>
/// Orchestrates the money movement over the life of an order: place, authorize (hold),
/// fulfil (capture), cancel (void) and refund. Each action is separately invocable and
/// idempotent in effect. All actions load the order scoped to its owner unless it is an
/// operator action (fulfil/cancel act on any order).
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Places an order from catalog items for the given shopper, awaiting payment.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines,
        ShipToAddressInput? shipTo, CancellationToken ct = default);

    /// <summary>
    /// Authorizes the order total (places the hold) using either raw <paramref name="card"/> details
    /// or one of the shopper's saved cards (<paramref name="paymentMethodId"/>). Idempotent: a repeat
    /// while already authorized returns the existing hold without charging again.
    /// </summary>
    Task<Order> AuthorizeAsync(int orderId, string buyerId, PayPalCard? card, int? paymentMethodId,
        CancellationToken ct = default);

    /// <summary>
    /// Operator action: captures the held money at fulfilment. A stale authorization is renewed
    /// (reauthorized) rather than failing outright; if it can no longer be renewed, an operator-actionable
    /// error is raised. Idempotent: a repeat once captured returns the existing capture.
    /// </summary>
    Task<Order> FulfilAsync(int orderId, CancellationToken ct = default);

    /// <summary>Operator action: releases the held money before fulfilment.</summary>
    Task<Order> CancelAsync(int orderId, CancellationToken ct = default);

    /// <summary>
    /// Refunds a captured payment, fully or partially, never exceeding what remains refundable.
    /// The <paramref name="idempotencyKey"/> makes a repeated request safe; two distinct keys are
    /// two legitimate partial refunds. Returns the (new or existing) refund.
    /// </summary>
    Task<PaymentRefund> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey,
        CancellationToken ct = default);

    /// <summary>The shopper's orders with their payment state, newest first.</summary>
    Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default);
}
