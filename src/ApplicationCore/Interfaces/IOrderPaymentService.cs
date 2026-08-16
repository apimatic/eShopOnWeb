using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A single line of a placed order: a catalog item and how many.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>
/// How the shopper wants to pay: either raw card details for a one-off payment, or the id of one
/// of their saved cards. Exactly one must be supplied.
/// </summary>
public record PaymentInstrument(CardPaymentDetails? Card, int? SavedCardId);

/// <summary>An order paired with its payment, for the my-orders listing.</summary>
public record OrderWithPayment(Order Order, OrderPayment Payment);

/// <summary>
/// Orchestrates the pay-for-an-order flow (place, authorize, fulfil/capture, cancel/void, refund)
/// and the saved-card flow, coordinating the domain, the repositories and the payment gateway.
/// </summary>
public interface IOrderPaymentService
{
    Task<OrderPayment> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, Address? shipToAddress,
        CancellationToken cancellationToken);

    Task<OrderPayment> AuthorizeAsync(string buyerId, int orderId, PaymentInstrument instrument,
        CancellationToken cancellationToken);

    /// <summary>Operator action: fulfil the order, capturing the money (renewing a stale hold first).</summary>
    Task<OrderPayment> FulfilAsync(int orderId, CancellationToken cancellationToken);

    /// <summary>Operator action: cancel before fulfilment, releasing any held funds.</summary>
    Task<OrderPayment> CancelAsync(int orderId, CancellationToken cancellationToken);

    Task<PaymentRefund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<OrderWithPayment>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken);

    Task<SavedCard> SaveCardAsync(string buyerId, CardPaymentDetails card, string? alias,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SavedCard>> GetSavedCardsAsync(string buyerId, CancellationToken cancellationToken);

    Task DeleteSavedCardAsync(string buyerId, int savedCardId, CancellationToken cancellationToken);

    /// <summary>Operator action: reconcile PayPal's transactions against eShop payments over a range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken);
}
