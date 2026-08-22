using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed class ReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public IReadOnlyList<ReconciliationRow> Matched { get; init; } = Array.Empty<ReconciliationRow>();
    public IReadOnlyList<ProviderTransaction> PayPalOnly { get; init; } = Array.Empty<ProviderTransaction>();
    public IReadOnlyList<ReconciliationOrderSummary> EshopOnly { get; init; } = Array.Empty<ReconciliationOrderSummary>();
}

public sealed class ReconciliationRow
{
    public int OrderId { get; init; }
    public string? PayPalTransactionId { get; init; }
    public string? MatchReason { get; init; }
}

public sealed class ReconciliationOrderSummary
{
    public int OrderId { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? PayPalOrderId { get; init; }
    public string? AuthorizationId { get; init; }
    public string? CaptureId { get; init; }
    public decimal Total { get; init; }
}

public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
