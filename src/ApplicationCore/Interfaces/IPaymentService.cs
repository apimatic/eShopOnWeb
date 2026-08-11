using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A single catalog line in a placed order.</summary>
public record OrderLineInput(int CatalogItemId, int Quantity);

/// <summary>
/// How a shopper wants to pay: either raw card details for a one-off payment, or one of the shopper's
/// saved cards (by its id). Exactly one must be supplied.
/// </summary>
public record PaymentInstrument(PayPalCardDetails? Card, int? SavedCardId)
{
    public bool IsSavedCard => SavedCardId.HasValue;
}

/// <summary>An order paired with its payment state, for the shopper's own listing.</summary>
public record OrderWithPayment(Order Order, Payment? Payment);

/// <summary>
/// Orchestrates the pay-for-an-order flow: place, authorize (hold), fulfil (capture), cancel (void),
/// refund, list, and reconcile. All shopper-scoped operations act only on the caller's own data.
/// </summary>
public interface IPaymentService
{
    /// <summary>Places an order from catalog lines and creates its payment in the awaiting-payment state.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineInput> lines, Address shipToAddress,
        CancellationToken cancellationToken = default);

    /// <summary>Authorizes the order total (places a hold). Idempotent: a double-click never holds twice.</summary>
    Task<Payment> AuthorizeOrderAsync(string buyerId, int orderId, PaymentInstrument instrument,
        CancellationToken cancellationToken = default);

    /// <summary>Operator action: fulfils the order, capturing the money (renewing a stale hold if needed).</summary>
    Task<Payment> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: cancels before fulfilment, releasing the held funds.</summary>
    Task<Payment> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a captured payment, in full or in part, under a caller-supplied idempotency key.</summary>
    Task<PaymentRefund> RefundOrderAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>The caller's orders with their payment state.</summary>
    Task<IReadOnlyList<OrderWithPayment>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: reconciles PayPal's transaction record against eShop orders for a date range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
