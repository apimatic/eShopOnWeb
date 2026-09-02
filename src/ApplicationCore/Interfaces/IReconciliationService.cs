using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class ReconciliationEntry
{
    public string TransactionId { get; set; } = string.Empty;
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public decimal? Fee { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? InitiationTime { get; set; }
    public string? InvoiceId { get; set; }
    public int? MatchedOrderId { get; set; }
    public int? MatchedPaymentId { get; set; }

    /// <summary>"matched" when lined up with an eShop order, "paypal-only" otherwise.</summary>
    public string Match { get; set; } = "paypal-only";
}

public class UnmatchedPayment
{
    public int OrderId { get; set; }
    public int PaymentId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string Match { get; set; } = "eshop-only";
}

public class ReconciliationReport
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationEntry> Transactions { get; set; } = new();
    public List<UnmatchedPayment> PaymentsWithoutPayPalTransaction { get; set; } = new();
}

public interface IReconciliationService
{
    Task<ReconciliationReport> GetReportAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
