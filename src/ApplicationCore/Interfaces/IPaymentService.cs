using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentService
{
    /// <summary>Authorizes (holds) the order total, with full card details or a saved card. Idempotent per order.</summary>
    Task<Payment> PayOrderAsync(int orderId, string buyerId, CardDetails? card, int? savedCardId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: captures the held funds, renewing a stale authorization first when needed. Idempotent per order.</summary>
    Task<Payment> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: cancels before fulfilment, releasing the held funds. Returns the payment if one exists.</summary>
    Task<Payment?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a captured payment in full (amount null) or in part. Idempotent per idempotencyKey.</summary>
    Task<PaymentRefund> RefundOrderAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey, string? note, CancellationToken cancellationToken = default);

    Task<SavedCard> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SavedCard>> ListSavedCardsAsync(string buyerId, CancellationToken cancellationToken = default);
    Task DeleteSavedCardAsync(int savedCardId, string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Operator report: PayPal's transactions for the range lined up against eShop payments.</summary>
    Task<ReconciliationResult> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public class ReconciliationResult
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationTransaction> Transactions { get; set; } = new List<ReconciliationTransaction>();
    public List<ReconciliationLocalPayment> LocalPaymentsNotInPayPal { get; set; } = new List<ReconciliationLocalPayment>();
}

public class ReconciliationTransaction
{
    public string? TransactionId { get; set; }
    public string? ReferenceId { get; set; }
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? Fee { get; set; }
    public DateTimeOffset? Time { get; set; }
    public string? InvoiceId { get; set; }
    public int? MatchedOrderId { get; set; }

    /// <summary>"matched" when lined up with an eShop order, "paypal-only" when PayPal knows of it and eShop does not.</summary>
    public string Match { get; set; } = "paypal-only";
}

public class ReconciliationLocalPayment
{
    public int OrderId { get; set; }
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
