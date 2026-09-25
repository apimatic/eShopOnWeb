using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the additive payment capability: placing an order awaiting payment, authorizing, fulfilling
/// (capture, renewing a stale authorization), cancelling, refunding, listing the caller's orders, saving and
/// reusing cards, and operator reconciliation. Enforces the state machine, ownership and idempotency.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Place an order (reusing Order/OrderItem) for the caller; it starts awaiting payment. Returns the order id.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, Address shipToAddress,
        CancellationToken cancellationToken);

    /// <summary>Authorize (hold) the order total with a one-off card or one of the caller's saved cards.</summary>
    Task<OrderPayment> PayAsync(string buyerId, int orderId, PayInstruction instruction, CancellationToken cancellationToken);

    /// <summary>Operator: fulfil the order — capture the held funds (renewing a stale authorization first if needed).</summary>
    Task<OrderPayment> FulfilAsync(int orderId, CancellationToken cancellationToken);

    /// <summary>Operator: cancel before fulfilment — release the held funds.</summary>
    Task<OrderPayment> CancelAsync(int orderId, CancellationToken cancellationToken);

    /// <summary>Refund the caller's captured order, fully or partially, under a caller-supplied idempotency key.</summary>
    Task<OrderRefund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken);

    /// <summary>The caller's orders with payment state.</summary>
    Task<IReadOnlyList<OrderPayment>> GetMyPaymentsAsync(string buyerId, CancellationToken cancellationToken);

    Task<OrderPayment?> GetPaymentAsync(int orderId, CancellationToken cancellationToken);

    /// <summary>Operator: line PayPal's transactions up against eShop orders over a date range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    // Saved cards -------------------------------------------------------------------------------
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken);
    Task<IReadOnlyList<SavedPaymentMethod>> GetSavedCardsAsync(string buyerId, CancellationToken cancellationToken);
    Task DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken);
}

/// <summary>A requested line: a catalog item and a quantity.</summary>
public sealed record OrderLine(int CatalogItemId, int Quantity);

/// <summary>How to pay: exactly one of a one-off card or a saved card id.</summary>
public sealed record PayInstruction
{
    public CardDetails? Card { get; init; }
    public int? SavedPaymentMethodId { get; init; }
}

public sealed record ReconciliationReport
{
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }
    public required IReadOnlyList<ReconciliationMatch> Matched { get; init; }
    /// <summary>Transactions PayPal knows about that no eShop order matched.</summary>
    public required IReadOnlyList<PayPalTransaction> PayPalOnly { get; init; }
    /// <summary>eShop captured payments PayPal did not report (may be empty due to reporting lag).</summary>
    public required IReadOnlyList<ReconciliationOrder> EShopOnly { get; init; }
    public required int PayPalPagesRetrieved { get; init; }
    public required int WindowsQueried { get; init; }
}

public sealed record ReconciliationMatch
{
    public required int OrderId { get; init; }
    public required string InvoiceId { get; init; }
    public string? CaptureId { get; init; }
    public string? PayPalTransactionId { get; init; }
    public decimal? EShopAmount { get; init; }
    public decimal? PayPalAmount { get; init; }
    public bool AmountsAgree { get; init; }
    public string? PayPalStatus { get; init; }
}

public sealed record ReconciliationOrder
{
    public required int OrderId { get; init; }
    public required string InvoiceId { get; init; }
    public string? CaptureId { get; init; }
    public decimal? CapturedAmount { get; init; }
}
