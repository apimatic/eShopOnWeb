using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>One line of a placed order: what to buy, and how many.</summary>
public sealed record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>
/// Placing, paying for and settling orders. Each action is separately invocable — nothing here
/// pays and fulfils in one step.
/// </summary>
public interface IPaymentService
{
    /// <summary>The configured currency every amount is denominated in.</summary>
    string CurrencyCode { get; }

    /// <summary>Places an order from catalog items. No money moves; the order awaits payment.</summary>
    Task<OrderView> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines,
        Address shipToAddress, CancellationToken cancellationToken);

    /// <summary>
    /// Authorizes the order total — puts a hold on the money without taking it. Repeating the call
    /// for an already-authorized order returns the existing hold rather than placing a second one.
    /// </summary>
    Task<PaymentView> AuthorizeAsync(string buyerId, int orderId, PaymentInstrument instrument,
        CancellationToken cancellationToken);

    /// <summary>
    /// Operator action: marks the order fulfilled and captures the held money, renewing a hold that
    /// has gone stale first. Repeating the call returns the existing capture.
    /// </summary>
    Task<PaymentView> FulfilAsync(int orderId, CancellationToken cancellationToken);

    /// <summary>
    /// Operator action: cancels before fulfilment, releasing any held funds so no money ever moved.
    /// </summary>
    Task<PaymentView?> CancelAsync(int orderId, CancellationToken cancellationToken);

    /// <summary>
    /// Refunds a captured payment, in full or in part. The idempotency key is the caller's: repeating
    /// a request under the same key returns the first refund instead of refunding twice.
    /// </summary>
    Task<RefundView> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken);

    /// <summary>The caller's own orders, each with its payment state.</summary>
    Task<IReadOnlyList<OrderView>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken);
}

/// <summary>Saved cards, always scoped to the shopper who saved them.</summary>
public interface IPaymentMethodService
{
    Task<SavedCardView> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken);

    Task<IReadOnlyList<SavedCardView>> ListAsync(string buyerId, CancellationToken cancellationToken);

    /// <summary>Removes a saved card. Returns false when the caller has no such card.</summary>
    Task<bool> DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken);
}

/// <summary>Operator action: lines PayPal's own record of transactions up against eShop's.</summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> BuildReportAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken);
}
