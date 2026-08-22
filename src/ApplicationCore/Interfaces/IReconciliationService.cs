using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}

public sealed class ReconciliationReport
{
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }
    public required IReadOnlyList<ReconciliationMatch> Matched { get; init; }
    public required IReadOnlyList<PayPalTransactionRecord> PayPalOnly { get; init; }
    public required IReadOnlyList<EshopUnmatchedOrder> EshopOnly { get; init; }
}

public sealed class ReconciliationMatch
{
    public int OrderId { get; init; }
    public string? PayPalTransactionId { get; init; }
    public string? InvoiceId { get; init; }
    public string? OrderStatus { get; init; }
    public string? PayPalStatus { get; init; }
    public decimal? OrderTotal { get; init; }
    public string? PayPalAmount { get; init; }
}

public sealed class EshopUnmatchedOrder
{
    public int OrderId { get; init; }
    public string? Status { get; init; }
    public decimal Total { get; init; }
    public string? PayPalOrderId { get; init; }
    public string? CaptureId { get; init; }
    public DateTimeOffset OrderDate { get; init; }
}
