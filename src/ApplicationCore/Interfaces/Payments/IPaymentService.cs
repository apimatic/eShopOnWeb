using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>A catalog line for a new order: which catalog item and how many.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>
/// How to pay an order: either raw card details for a one-off payment, or the id of one of the
/// shopper's saved cards. Exactly one must be supplied. <see cref="SaveCard"/> vaults the one-off
/// card for reuse.
/// </summary>
public record PayInstruction(CardDetails? Card, int? SavedPaymentMethodId, bool SaveCard);

/// <summary>An order paired with its payment state, for the my-orders view.</summary>
public record OrderWithPayment(Order Order, Payment? Payment);

/// <summary>One line of the reconciliation report: a PayPal transaction lined up (or not) with an eShop order.</summary>
public record ReconciliationEntry(
    string? PayPalTransactionId,
    string? InvoiceId,
    int? OrderId,
    decimal? PayPalAmount,
    decimal? EShopAmount,
    string CurrencyCode,
    string? PayPalStatus,
    string MatchStatus,
    DateTimeOffset? TransactionDate);

/// <summary>The reconciliation report for a date range.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int MatchedCount,
    int PayPalOnlyCount,
    int EShopOnlyCount,
    IReadOnlyList<ReconciliationEntry> Entries);

public interface IPaymentService
{
    /// <summary>Places a new order for the buyer from catalog items, awaiting payment.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, Address shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>Authorizes (holds) the order total. Shopper-scoped to the order's owner.</summary>
    Task<Payment> AuthorizeAsync(string buyerId, int orderId, PayInstruction instruction, CancellationToken cancellationToken = default);

    /// <summary>Fulfils the order, capturing the held funds. Operator action (any order).</summary>
    Task<Payment> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Cancels the order before fulfilment, releasing the hold. Operator action (any order).</summary>
    Task<Payment> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a captured payment, in full or in part, idempotent under the caller's key. Shopper-scoped.</summary>
    Task<(Payment Payment, PaymentRefund Refund)> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>The payment for a caller's order, or throws if not the caller's.</summary>
    Task<Payment> GetPaymentForBuyerAsync(string buyerId, int orderId, CancellationToken cancellationToken = default);

    /// <summary>The caller's orders with their payment state.</summary>
    Task<IReadOnlyList<OrderWithPayment>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Reconciles PayPal's transaction record against eShop orders over a date range. Operator action.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
