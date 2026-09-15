using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed class ReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public IReadOnlyList<MatchedPayment> Matched { get; init; } = [];
    public IReadOnlyList<PayPalReportedTransaction> PayPalOnly { get; init; } = [];
    public IReadOnlyList<EShopPaymentRecord> EShopOnly { get; init; } = [];
}

public sealed class MatchedPayment
{
    public int OrderId { get; init; }
    public string? PayPalTransactionId { get; init; }
    public string? InvoiceId { get; init; }
    public string MatchReason { get; init; } = string.Empty;
}

public sealed class EShopPaymentRecord
{
    public int OrderId { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? PayPalOrderId { get; init; }
    public string? PayPalAuthorizationId { get; init; }
    public string? PayPalCaptureId { get; init; }
    public DateTimeOffset OrderDate { get; init; }
}

public interface IPaymentReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
