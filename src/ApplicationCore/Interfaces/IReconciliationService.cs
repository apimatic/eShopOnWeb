using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class ReconciliationEntry
{
    public string PayPalTransactionId { get; set; } = string.Empty;
    public string? EventCode { get; set; }
    public string? PayPalStatus { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? FeeAmount { get; set; }
    public DateTimeOffset? TransactionDate { get; set; }
    public int? OrderId { get; set; }
    public int? PaymentId { get; set; }
    public string MatchStatus { get; set; } = string.Empty; // Matched | OnlyInPayPal | OnlyInEShop
}

public class ReconciliationReport
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationEntry> Entries { get; set; } = new List<ReconciliationEntry>();
}

public interface IReconciliationService
{
    Task<ReconciliationReport> GetReconciliationAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
