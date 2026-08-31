using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

public class ReconciliationReport
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciledTransaction> Transactions { get; set; } = new List<ReconciledTransaction>();
    public List<UnmatchedPayment> PaymentsMissingFromPayPal { get; set; } = new List<UnmatchedPayment>();
}

public class ReconciledTransaction
{
    public string TransactionId { get; set; } = string.Empty;
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? Time { get; set; }
    public int? OrderId { get; set; }
    public int? PaymentId { get; set; }
    public bool MatchedToOrder => OrderId.HasValue;
}

public class UnmatchedPayment
{
    public int OrderId { get; set; }
    public int PaymentId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
