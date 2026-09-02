using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedPaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class RefundResult
{
    public PaymentRefund Refund { get; set; } = null!;
    public decimal TotalRefunded { get; set; }
    public decimal RefundableAmount { get; set; }
}

public class ReconciliationTransaction
{
    public string TransactionId { get; set; } = string.Empty;
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? FeeAmount { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomId { get; set; }
    public string? ReferenceId { get; set; }
    public string? ReferenceIdType { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
    /// <summary>The eShop order this PayPal transaction lines up with, if any.</summary>
    public int? MatchedOrderId { get; set; }
    /// <summary>How the match was established (capture id, authorization id, refund id, order id, invoice/custom id).</summary>
    public string? MatchBasis { get; set; }
}

public class ReconciliationOrder
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string? Currency { get; set; }
    public string? PayPalOrderId { get; set; }
    public string? PayPalAuthorizationId { get; set; }
    public string? PayPalCaptureId { get; set; }
    public List<string> PayPalRefundIds { get; set; } = new();
    /// <summary>True when at least one PayPal-reported transaction lines up with this order.</summary>
    public bool SeenInPayPalReport { get; set; }
    public List<string> MatchedTransactionIds { get; set; } = new();
}

public class ReconciliationReport
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationTransaction> PayPalTransactions { get; set; } = new();
    public List<ReconciliationOrder> Orders { get; set; } = new();
}

/// <summary>
/// Orchestrates the payment lifecycle of an order (authorize at checkout, capture at
/// fulfilment, void on cancel, refund on return) and the shopper's saved cards.
/// </summary>
public interface IPaymentService
{
    /// <summary>Authorizes the order total with either one-off card details or one of the shopper's saved cards.</summary>
    Task<Order> PayOrderAsync(string buyerId, int orderId, CardDetails? card, int? savedPaymentMethodId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: captures the held funds, renewing a stale authorization when possible.</summary>
    Task<Order> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: cancels before fulfilment, releasing the held funds.</summary>
    Task<Order> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a fulfilled order, in full (amount null) or in part, under a caller-supplied idempotency key.</summary>
    Task<RefundResult> RefundOrderAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, string? noteToPayer, CancellationToken cancellationToken = default);

    Task<SavedPaymentMethod> SavePaymentMethodAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SavedPaymentMethod>> ListPaymentMethodsAsync(string buyerId, CancellationToken cancellationToken = default);
    Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);

    Task<ReconciliationReport> GetReconciliationReportAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
