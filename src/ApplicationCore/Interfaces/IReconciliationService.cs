using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record ReconciliationEntry(
    string? PayPalTransactionId,
    int? OrderId,
    decimal? PayPalAmount,
    decimal? EShopAmount,
    string? PayPalStatus,
    string MatchStatus);

/// <summary>Lines up PayPal's own transaction records for a date range against this app's orders.</summary>
public interface IReconciliationService
{
    Task<IReadOnlyList<ReconciliationEntry>> GetReconciliationReportAsync(DateTimeOffset from, DateTimeOffset to);
}
