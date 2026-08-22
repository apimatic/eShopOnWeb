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
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }
    public required int PayPalTransactionCount { get; init; }
    public required int EshopPaymentCount { get; init; }
    public required IReadOnlyList<ReconciliationMatch> Matched { get; init; }
    public required IReadOnlyList<ReconciliationPayPalOnly> PayPalOnly { get; init; }
    public required IReadOnlyList<ReconciliationEshopOnly> EshopOnly { get; init; }
}

public sealed class ReconciliationMatch
{
    public required int OrderId { get; init; }
    public required string PayPalTransactionId { get; init; }
    public string? EshopPaymentId { get; init; }
    public string? MatchReason { get; init; }
}

public sealed class ReconciliationPayPalOnly
{
    public required string PayPalTransactionId { get; init; }
    public string? Status { get; init; }
    public string? Amount { get; init; }
    public string? Currency { get; init; }
    public string? CustomField { get; init; }
    public string? InvoiceId { get; init; }
}

public sealed class ReconciliationEshopOnly
{
    public required int OrderId { get; init; }
    public required string Kind { get; init; }
    public required string PayPalId { get; init; }
    public string? Status { get; init; }
}
