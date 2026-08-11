using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Lines PayPal's own record of transactions for a date range up against eShop's orders, so a
/// payment one side knows about and the other doesn't is visible.
/// </summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

/// <summary>One PayPal transaction matched to an eShop order.</summary>
public record ReconciledEntry(int OrderId, string PaymentState, PayPalTransaction Transaction);

/// <summary>An eShop order/payment for which no PayPal transaction was found in the range.</summary>
public record UnmatchedOrder(
    int OrderId,
    string PaymentState,
    decimal Amount,
    string Currency,
    string? AuthorizationId,
    string? CaptureId);

/// <summary>
/// A reconciliation report over a date range. <see cref="PayPalOnly"/> are transactions PayPal knows
/// about but eShop could not match; <see cref="EShopOnly"/> are eShop payments PayPal has not
/// reported (expected for very recent activity, since PayPal's reporting lags).
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciledEntry> Matched,
    IReadOnlyList<PayPalTransaction> PayPalOnly,
    IReadOnlyList<UnmatchedOrder> EShopOnly)
{
    public int PayPalTransactionCount => Matched.Count + PayPalOnly.Count;
}
