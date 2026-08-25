using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public enum ReconciliationMatchStatus
{
    /// <summary>Known to both PayPal and eShop, and they agree.</summary>
    Matched,
    /// <summary>PayPal has a record of this transaction, but eShop has no corresponding row.</summary>
    MissingInEShop,
    /// <summary>eShop has a payment/capture/refund it expected PayPal to know about, but PayPal's
    /// report for this range does not contain it.</summary>
    MissingInPayPal
}

public record ReconciliationEntry(
    ReconciliationMatchStatus Status,
    string? PayPalTransactionId,
    string? PayPalEventCode,
    string? PayPalStatus,
    decimal? PayPalAmount,
    int? OrderId,
    string? EShopReference,
    decimal? EShopAmount,
    string? CurrencyCode);

public record ReconciliationReport(DateTimeOffset From, DateTimeOffset To, IReadOnlyList<ReconciliationEntry> Entries);

/// <summary>Lines PayPal's own transaction report up against eShop's orders for a date range so
/// discrepancies in either direction are visible.</summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> BuildReportAsync(DateTimeOffset from, DateTimeOffset to);
}
