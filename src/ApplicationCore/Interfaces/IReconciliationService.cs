using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class ReconciliationEntry
{
    public string? TransactionId { get; set; }
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? FeeAmount { get; set; }
    public DateTimeOffset? TransactionTime { get; set; }

    /// <summary>The eShop order this transaction lines up with, when known.</summary>
    public int? OrderId { get; set; }

    /// <summary>"Matched" when the transaction lines up with an eShop order, "MissingInEShop" otherwise.</summary>
    public string MatchStatus { get; set; } = string.Empty;
}

public class UnmatchedPayment
{
    public int OrderId { get; set; }
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class ReconciliationReport
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }

    /// <summary>PayPal's own record of transactions for the range, lined up against eShop orders.</summary>
    public List<ReconciliationEntry> Transactions { get; set; } = new List<ReconciliationEntry>();

    /// <summary>Payments eShop knows about (created in the range) that PayPal's report does not list.</summary>
    public List<UnmatchedPayment> MissingFromPayPal { get; set; } = new List<UnmatchedPayment>();
}

public interface IReconciliationService
{
    Task<ReconciliationReport> GetReportAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
