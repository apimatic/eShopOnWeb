using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IReconciliationService
{
    /// <summary>
    /// Line the provider's own transaction records for a date range up against eShop orders.
    /// Covers the whole range (all report pages).
    /// </summary>
    Task<ReconciliationReport> GetReportAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}

public class ReconciliationReport
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationEntry> Transactions { get; set; } = new List<ReconciliationEntry>();

    /// <summary>eShop orders with payments that have no matching provider transaction in the range.</summary>
    public List<ReconciliationOrder> OrdersMissingFromProviderReport { get; set; } = new List<ReconciliationOrder>();
}

public class ReconciliationEntry
{
    public string? TransactionId { get; set; }
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? Fee { get; set; }
    public string? InvoiceId { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
    public DateTimeOffset? UpdatedDate { get; set; }

    /// <summary>The eShop order this transaction lines up with, or null when the provider
    /// knows about a payment eShop doesn't.</summary>
    public int? MatchedOrderId { get; set; }
}

public class ReconciliationOrder
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
    public decimal? CapturedAmount { get; set; }
    public string? Currency { get; set; }
}
