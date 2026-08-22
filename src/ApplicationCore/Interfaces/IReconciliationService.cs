using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public sealed class ReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public IReadOnlyList<ReconciliationMatch> Matched { get; init; } = Array.Empty<ReconciliationMatch>();
    public IReadOnlyList<PayPalTransactionRecord> PayPalOnly { get; init; } = Array.Empty<PayPalTransactionRecord>();
    public IReadOnlyList<EshopPaymentSummary> EshopOnly { get; init; } = Array.Empty<EshopPaymentSummary>();
}

public sealed class ReconciliationMatch
{
    public int OrderId { get; init; }
    public PayPalTransactionRecord Transaction { get; init; } = new();
}

public sealed class EshopPaymentSummary
{
    public int OrderId { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? PayPalOrderId { get; init; }
    public string? AuthorizationId { get; init; }
    public string? CaptureId { get; init; }
    public IReadOnlyList<string> RefundIds { get; init; } = Array.Empty<string>();
}
