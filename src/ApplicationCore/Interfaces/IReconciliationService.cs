using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>One matched or unmatched line in a reconciliation report.</summary>
public record ReconciliationEntry(
    string Source,          // "matched", "missing-in-eshop", or "missing-in-paypal"
    int? OrderId,
    string? InvoiceId,
    string? PayPalOrderId,
    string? PayPalTransactionId,
    string? EShopStatus,
    string? PayPalStatus,
    decimal? EShopAmount,
    decimal? PayPalAmount,
    string CurrencyCode);

/// <summary>The full reconciliation report for a date range.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int EShopPaymentCount,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> MissingInEShop,
    IReadOnlyList<ReconciliationEntry> MissingInPayPal,
    string Note);

/// <summary>
/// Builds a report lining PayPal's own transaction records up against eShop orders for a date
/// range, so a payment PayPal knows about and eShop doesn't — or the reverse — is visible.
/// </summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
