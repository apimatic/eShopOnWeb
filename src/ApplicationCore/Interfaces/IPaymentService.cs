using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class OrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ReconciliationEntry
{
    public string TransactionId { get; set; } = string.Empty;
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? FeeAmount { get; set; }
    public DateTimeOffset? TransactionDate { get; set; }
    public string? InvoiceId { get; set; }
    public int? MatchedOrderId { get; set; }
    public int? MatchedPaymentId { get; set; }

    /// <summary>Matched | NotKnownToEShop</summary>
    public string MatchStatus { get; set; } = string.Empty;
}

public class UnmatchedEShopPayment
{
    public int OrderId { get; set; }
    public int PaymentId { get; set; }
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
    public string Status { get; set; } = string.Empty;

    /// <summary>NotReportedByPayPal</summary>
    public string MatchStatus { get; set; } = "NotReportedByPayPal";
}

public class ReconciliationReport
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }
    public List<ReconciliationEntry> Transactions { get; set; } = new();
    public List<UnmatchedEShopPayment> UnmatchedEShopPayments { get; set; } = new();
    public int TotalPayPalTransactions { get; set; }
    public int TotalMatched { get; set; }
    public int TotalUnmatchedPayPal { get; set; }
    public int TotalUnmatchedEShop { get; set; }
}

public interface IPaymentService
{
    Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderItemRequest> items, CancellationToken cancellationToken = default);

    /// <summary>Places the hold. Returns null when the order does not belong to the buyer.</summary>
    Task<Payment?> PayOrderAsync(string buyerId, int orderId, CardDetails? card, int? paymentMethodId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: takes the money. Renews a stale authorization when possible. Null when the order does not exist.</summary>
    Task<Payment?> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: releases the hold before fulfilment. Null when the order does not exist.</summary>
    Task<(Order Order, Payment? Payment)?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a captured payment, in full or in part. Idempotent per idempotencyKey.</summary>
    Task<PaymentRefund?> RefundOrderAsync(string buyerId, bool isOperator, int orderId, decimal? amount,
        string idempotencyKey, string? noteToPayer, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<(Order Order, Payment? Payment)>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedPaymentMethod>> GetSavedCardsAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Returns false when the card does not exist or belongs to another shopper.</summary>
    Task<bool> DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);

    Task<ReconciliationReport> GetReconciliationAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
