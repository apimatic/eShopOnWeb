using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A PayPal transaction that lines up with an eShop order/payment.</summary>
public record ReconciliationMatch(
    int OrderId,
    string EShopPaymentStatus,
    string PayPalTransactionId,
    string EventCode,
    decimal Amount,
    string CurrencyCode,
    DateTimeOffset Date);

/// <summary>An eShop payment record with no matching PayPal transaction in the range.</summary>
public record ReconciliationEShopOnlyEntry(
    int OrderId,
    string Kind,
    string PayPalId,
    decimal Amount,
    string EShopPaymentStatus);

/// <summary>
/// Reconciliation report over a date range: PayPal's own transactions lined up against eShop
/// orders, exposing anything one side knows about and the other doesn't.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string CurrencyCode,
    int PayPalTransactionCount,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<PayPalTransaction> InPayPalNotInEShop,
    IReadOnlyList<ReconciliationEShopOnlyEntry> InEShopNotInPayPal);

/// <summary>Operator action: reconcile eShop orders against PayPal's record for a date range.</summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
