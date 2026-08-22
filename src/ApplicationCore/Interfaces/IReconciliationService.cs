using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public sealed class ReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public IReadOnlyList<PayPalReportedTransaction> PayPalTransactions { get; init; } = Array.Empty<PayPalReportedTransaction>();
    public IReadOnlyList<ReconciliationMatch> Matches { get; init; } = Array.Empty<ReconciliationMatch>();
    public IReadOnlyList<PayPalReportedTransaction> PayPalOnly { get; init; } = Array.Empty<PayPalReportedTransaction>();
    public IReadOnlyList<EshopReconciliationEntry> EshopOnly { get; init; } = Array.Empty<EshopReconciliationEntry>();
}

public sealed class ReconciliationMatch
{
    public PayPalReportedTransaction PayPal { get; init; } = null!;
    public EshopReconciliationEntry Eshop { get; init; } = null!;
}

public sealed class EshopReconciliationEntry
{
    public int OrderId { get; init; }
    public OrderStatus Status { get; init; }
    public string? PayPalOrderId { get; init; }
    public string? AuthorizationId { get; init; }
    public string? CaptureId { get; init; }
    public IReadOnlyList<string> RefundIds { get; init; } = Array.Empty<string>();
    public string? InvoiceId { get; init; }
}
