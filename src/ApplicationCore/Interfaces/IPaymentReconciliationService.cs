using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}

public sealed class ReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public IReadOnlyList<PayPalReportedTransaction> PayPalTransactions { get; init; } = Array.Empty<PayPalReportedTransaction>();
    public IReadOnlyList<EshopPaymentRecord> EshopPayments { get; init; } = Array.Empty<EshopPaymentRecord>();
    public IReadOnlyList<ReconciliationMatch> Matched { get; init; } = Array.Empty<ReconciliationMatch>();
    public IReadOnlyList<PayPalReportedTransaction> PayPalOnly { get; init; } = Array.Empty<PayPalReportedTransaction>();
    public IReadOnlyList<EshopPaymentRecord> EshopOnly { get; init; } = Array.Empty<EshopPaymentRecord>();
}

public sealed class EshopPaymentRecord
{
    public int OrderId { get; init; }
    public string BuyerId { get; init; } = string.Empty;
    public string PaymentStatus { get; init; } = string.Empty;
    public string? PayPalOrderId { get; init; }
    public string? PayPalAuthorizationId { get; init; }
    public string? PayPalCaptureId { get; init; }
    public decimal OrderTotal { get; init; }
    public decimal? CapturedAmount { get; init; }
    public DateTimeOffset OrderDate { get; init; }
}

public sealed class ReconciliationMatch
{
    public int OrderId { get; init; }
    public string? PayPalTransactionId { get; init; }
    public string MatchReason { get; init; } = string.Empty;
}
