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
    public required IReadOnlyList<ReconciliationMatch> Matches { get; init; }
    public required IReadOnlyList<GatewayTransaction> PayPalOnly { get; init; }
    public required IReadOnlyList<EshopUnmatchedPayment> EshopOnly { get; init; }
}

public sealed class ReconciliationMatch
{
    public required int OrderId { get; init; }
    public required string OrderStatus { get; init; }
    public required GatewayTransaction PayPalTransaction { get; init; }
}

public sealed class EshopUnmatchedPayment
{
    public required int OrderId { get; init; }
    public required string OrderStatus { get; init; }
    public string? PayPalOrderId { get; init; }
    public string? AuthorizationId { get; init; }
    public string? CaptureId { get; init; }
    public IReadOnlyList<string> RefundIds { get; init; } = Array.Empty<string>();
    public DateTimeOffset OrderDate { get; init; }
    public decimal Total { get; init; }
}
