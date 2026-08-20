using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public class ReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public IReadOnlyList<ReconciliationRow> Matches { get; init; } = Array.Empty<ReconciliationRow>();
    public IReadOnlyList<PayPalReportedTransaction> PaypalOnly { get; init; } = Array.Empty<PayPalReportedTransaction>();
    public IReadOnlyList<ReconciliationEshopOnly> EshopOnly { get; init; } = Array.Empty<ReconciliationEshopOnly>();
}

public class ReconciliationRow
{
    public int OrderId { get; init; }
    public string OrderStatus { get; init; } = string.Empty;
    public string? PayPalTransactionId { get; init; }
    public string? PayPalCaptureId { get; init; }
    public string? PayPalAuthorizationId { get; init; }
    public decimal? PaypalAmount { get; init; }
    public decimal? EshopAmount { get; init; }
    public string MatchReason { get; init; } = string.Empty;
}

public class ReconciliationEshopOnly
{
    public int OrderId { get; init; }
    public string OrderStatus { get; init; } = string.Empty;
    public string? PayPalOrderId { get; init; }
    public string? AuthorizationId { get; init; }
    public string? CaptureId { get; init; }
    public decimal Total { get; init; }
}
