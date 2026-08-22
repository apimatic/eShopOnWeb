using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class ReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public IReadOnlyList<ReconciliationRow> Matched { get; init; } = Array.Empty<ReconciliationRow>();
    public IReadOnlyList<PayPalReportedTransaction> PayPalOnly { get; init; } = Array.Empty<PayPalReportedTransaction>();
    public IReadOnlyList<EshopPaymentRecord> EshopOnly { get; init; } = Array.Empty<EshopPaymentRecord>();
}

public class ReconciliationRow
{
    public int OrderId { get; init; }
    public string MatchKey { get; init; } = string.Empty;
    public PayPalReportedTransaction PayPal { get; init; } = null!;
    public EshopPaymentRecord Eshop { get; init; } = null!;
}

public class EshopPaymentRecord
{
    public int OrderId { get; init; }
    public string BuyerId { get; init; } = string.Empty;
    public string PaymentStatus { get; init; } = string.Empty;
    public string? PayPalOrderId { get; init; }
    public string? AuthorizationId { get; init; }
    public string? CaptureId { get; init; }
    public string? RefundId { get; init; }
    public decimal? Amount { get; init; }
    public string? Currency { get; init; }
}

public interface IPaymentReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to);
}
