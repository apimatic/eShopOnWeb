using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Lines PayPal's own transaction report up against local orders so discrepancies in either
/// direction are visible.
/// </summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> GetReportAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}

public class ReconciliationReport
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }

    /// <summary>Every transaction PayPal reported for the range, with the local order it matched (if any).</summary>
    public List<ReconciliationLine> Transactions { get; set; } = new();

    /// <summary>PayPal transactions no local order claims.</summary>
    public List<ReconciliationLine> UnmatchedTransactions { get; set; } = new();

    /// <summary>Local orders with PayPal payment ids that never appeared in PayPal's report.</summary>
    public List<UnmatchedOrderLine> OrdersMissingFromPayPalReport { get; set; } = new();
}

public class ReconciliationLine
{
    public GatewayTransaction Transaction { get; set; } = default!;
    public int? OrderId { get; set; }
}

public class UnmatchedOrderLine
{
    public int OrderId { get; set; }
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
}
