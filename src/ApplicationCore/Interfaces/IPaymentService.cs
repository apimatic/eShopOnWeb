using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A requested order line: a catalog item and a quantity.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>
/// How to pay: either raw <see cref="Card"/> details for a one-off payment, or the id of one
/// of the shopper's <see cref="SavedPaymentMethodId"/> saved cards. Exactly one is expected.
/// </summary>
public class PaymentInstruction
{
    public CardDetails? Card { get; init; }
    public int? SavedPaymentMethodId { get; init; }
}

/// <summary>An order together with its payment state, for the caller's order list.</summary>
public record OrderPaymentView(Order Order, Entities.PaymentAggregate.Payment? Payment);

/// <summary>One reconciled row lining a PayPal transaction up against an eShop order.</summary>
public class ReconciliationLine
{
    public string? PayPalTransactionId { get; set; }
    public string? PayPalStatus { get; set; }
    public decimal? PayPalAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public string? InvoiceId { get; set; }
    public int? OrderId { get; set; }
    public string? EShopPaymentStatus { get; set; }
    public decimal? EShopAmount { get; set; }
}

/// <summary>A reconciliation report over a date range.</summary>
public class ReconciliationReport
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int PayPalTransactionCount { get; set; }
    public List<ReconciliationLine> Matched { get; set; } = new();
    public List<ReconciliationLine> InPayPalNotInEShop { get; set; } = new();
    public List<ReconciliationLine> InEShopNotInPayPal { get; set; } = new();
}

/// <summary>
/// Orchestrates the payment flows: placing orders, authorizing (holding) funds, fulfilling
/// (capturing), cancelling (voiding), refunding, saved cards and reconciliation. Each action
/// is separately invocable. Shopper-scoped methods take the caller's buyer id and act only on
/// that shopper's data; operator methods act across shoppers.
/// </summary>
public interface IPaymentService
{
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> items,
        Address? shipToAddress, CancellationToken ct = default);

    Task AuthorizeOrderAsync(int orderId, string buyerId, PaymentInstruction instruction,
        CancellationToken ct = default);

    /// <summary>Operator action: fulfil the order and capture the held funds.</summary>
    Task FulfilOrderAsync(int orderId, CancellationToken ct = default);

    /// <summary>Operator action: cancel before fulfilment, releasing held funds.</summary>
    Task CancelOrderAsync(int orderId, CancellationToken ct = default);

    /// <summary>Refunds a captured order, fully or partially; returns the new refund's id.</summary>
    Task<int> RefundOrderAsync(int orderId, string buyerId, decimal? amount,
        string idempotencyKey, CancellationToken ct = default);

    Task<IReadOnlyList<OrderPaymentView>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default);

    /// <summary>
    /// A single order with its payment state. When <paramref name="buyerId"/> is non-null the
    /// order must belong to that shopper; when null (operator context) ownership is not checked.
    /// </summary>
    Task<OrderPaymentView> GetOrderViewAsync(int orderId, string? buyerId, CancellationToken ct = default);

    Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct = default);

    Task<IReadOnlyList<PaymentMethod>> GetCardsAsync(string buyerId, CancellationToken ct = default);

    Task DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct = default);

    /// <summary>Operator action: reconcile PayPal's transactions against eShop orders.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
