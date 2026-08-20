using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payment;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class ReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public IReadOnlyList<ReconciliationMatch> Matched { get; init; } = Array.Empty<ReconciliationMatch>();
    public IReadOnlyList<GatewayTransaction> PaypalOnly { get; init; } = Array.Empty<GatewayTransaction>();
    public IReadOnlyList<EshopOrphan> EshopOnly { get; init; } = Array.Empty<EshopOrphan>();
}

public class ReconciliationMatch
{
    public int OrderId { get; init; }
    public string? CaptureId { get; init; }
    public string? AuthorizationId { get; init; }
    public GatewayTransaction Paypal { get; init; } = null!;
}

public class EshopOrphan
{
    public int OrderId { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? CaptureId { get; init; }
    public string? AuthorizationId { get; init; }
    public decimal Amount { get; init; }
}

public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
