using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Payments;

public sealed class ReconciliationReport
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string Currency { get; set; } = string.Empty;
    public List<FinixTransactionView> FinixTransactions { get; set; } = new();
    public List<EshopPaymentView> EshopPayments { get; set; } = new();
    public List<FinixTransactionView> UnmatchedFinix { get; set; } = new();
    public List<EshopReferenceView> UnmatchedEshop { get; set; } = new();
    public ReconciliationSummary Summary { get; set; } = new();
}

public sealed class FinixTransactionView
{
    public string Id { get; set; } = string.Empty;
    /// <summary>Authorization, Capture, or Refund.</summary>
    public string Kind { get; set; } = string.Empty;
    public long AmountCents { get; set; }
    public decimal Amount { get; set; }
    public string State { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset? CreatedAt { get; set; }
    public string? ParentId { get; set; }
    public long? FeeCents { get; set; }
    public int? MatchedOrderId { get; set; }
    public string? MatchedEntity { get; set; }
    public bool Matched { get; set; }
}

public sealed class EshopPaymentView
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public long AmountCents { get; set; }
    public long CapturedAmountCents { get; set; }
    public string? AuthorizationId { get; set; }
    public string? CaptureTransferId { get; set; }
    public List<string> RefundTransferIds { get; set; } = new();
}

public sealed class EshopReferenceView
{
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public sealed class ReconciliationSummary
{
    public int FinixTransactionCount { get; set; }
    public int EshopReferenceCount { get; set; }
    public int MatchedCount { get; set; }
    public int UnmatchedFinixCount { get; set; }
    public int UnmatchedEshopCount { get; set; }
}
