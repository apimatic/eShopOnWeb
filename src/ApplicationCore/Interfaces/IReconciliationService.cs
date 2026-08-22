using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

public sealed class ReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public IReadOnlyList<ReconciliationMatch> Items { get; init; } = Array.Empty<ReconciliationMatch>();
}

public sealed class ReconciliationMatch
{
    public PayPalTransactionRecord? PayPal { get; init; }
    public int? OrderId { get; init; }
    public string? EshopPaymentStatus { get; init; }
    public string Match { get; init; } = "unmatched";
}
