using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Lines up the payment processor's own record of transactions against eShop orders.
/// </summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> GetReconciliationAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

public class ReconciliationReport
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }

    /// <summary>Every transaction the processor reported for the range (all pages).</summary>
    public List<ReconciliationTransaction> Transactions { get; set; } = new();

    /// <summary>Processor transactions with no matching eShop payment.</summary>
    public List<ReconciliationTransaction> MissingInEShop { get; set; } = new();

    /// <summary>eShop payment records in the range that the processor did not report.</summary>
    public List<ReconciliationLocalRecord> MissingInPayPal { get; set; } = new();
}

public class ReconciliationTransaction
{
    public string? TransactionId { get; set; }
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public string? Amount { get; set; }
    public string? Currency { get; set; }
    public string? Fee { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
    public string? ReferenceId { get; set; }
    public string? InvoiceId { get; set; }

    /// <summary>The eShop order this transaction lines up with, if any.</summary>
    public int? OrderId { get; set; }

    /// <summary>What the transaction matched: "authorization", "capture" or "refund".</summary>
    public string? MatchedWith { get; set; }
}

public class ReconciliationLocalRecord
{
    public int OrderId { get; set; }

    /// <summary>"authorization", "capture" or "refund".</summary>
    public string RecordType { get; set; } = string.Empty;
    public string? ProcessorId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset? When { get; set; }
}
