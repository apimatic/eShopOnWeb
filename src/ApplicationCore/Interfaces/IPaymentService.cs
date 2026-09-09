using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A catalog line requested when placing an order.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>
/// Instrument for a <c>pay</c> request: exactly one of a one-off <see cref="Card"/> or a
/// <see cref="SavedCardId"/> naming one of the shopper's vaulted cards.
/// </summary>
public record PaymentInstrument(PayPalCardInput? Card, int? SavedCardId);

/// <summary>An order paired with its payment state, for read models.</summary>
public record OrderWithPayment(Order Order, Payment? Payment);

/// <summary>
/// Orchestrates the money movement over an order's lifetime: place, authorize (hold), fulfil
/// (capture), cancel (void) and refund. Every step is idempotent in effect.
/// </summary>
public interface IPaymentService
{
    /// <summary>
    /// Places an order for <paramref name="buyerId"/> from catalog lines, reusing the existing
    /// Order/OrderItem model, and opens a payment awaiting authorization. Returns the new order.
    /// </summary>
    Task<Order> PlaceOrderAsync(string buyerId, IEnumerable<OrderLineRequest> lines,
        CancellationToken ct = default);

    /// <summary>
    /// Authorizes the order total (a hold, not a charge) for the caller's own order. Idempotent:
    /// a repeat once authorized returns the existing hold rather than authorizing again.
    /// </summary>
    Task<Payment> AuthorizeAsync(string buyerId, int orderId, PaymentInstrument instrument,
        CancellationToken ct = default);

    /// <summary>
    /// Operator action: captures the held funds at fulfilment, renewing a stale hold first if
    /// needed. Idempotent: a repeat once captured returns the existing capture.
    /// </summary>
    Task<Payment> FulfilAsync(int orderId, CancellationToken ct = default);

    /// <summary>Operator action: releases the hold before capture so no money moves.</summary>
    Task<Payment> CancelAsync(int orderId, CancellationToken ct = default);

    /// <summary>
    /// Refunds the caller's captured order, in full or in part, guarded by an idempotency key.
    /// A repeat under the same key returns the existing refund; the total never exceeds the
    /// captured amount.
    /// </summary>
    Task<PaymentRefund> RefundAsync(string buyerId, int orderId, decimal? amount,
        string idempotencyKey, CancellationToken ct = default);

    /// <summary>The caller's orders with their payment state.</summary>
    Task<IReadOnlyList<OrderWithPayment>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default);
}
