using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Builds the reconciliation report: PayPal's own record of transactions for a date range lined up
/// against eShop orders, so a payment one side knows about and the other does not is visible.
/// </summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

/// <summary>A PayPal transaction that lines up with an eShop order.</summary>
public sealed record ReconciliationMatch(
    int EShopOrderId,
    string? PayPalTransactionId,
    string? EventCode,
    string? Status,
    decimal? Amount,
    string? Currency);

/// <summary>A PayPal transaction with no matching eShop order.</summary>
public sealed record UnmatchedPayPalTransaction(
    string? PayPalTransactionId,
    string? EventCode,
    string? Status,
    decimal? Amount,
    string? Currency,
    string? InvoiceId,
    DateTimeOffset? InitiationDate);

/// <summary>An eShop order whose captured payment was not found in PayPal's records for the range.</summary>
public sealed record UnmatchedEShopOrder(
    int EShopOrderId,
    string? MerchantReference,
    string? CaptureId,
    decimal Amount,
    string Currency);

/// <summary>The full reconciliation report for a date range.</summary>
public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<UnmatchedPayPalTransaction> InPayPalNotInEShop,
    IReadOnlyList<UnmatchedEShopOrder> InEShopNotInPayPal,
    string Note);
