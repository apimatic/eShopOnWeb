using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Builds a reconciliation report lining PayPal's transaction record up against eShop orders.</summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> BuildAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string Currency,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<PayPalTransactionRecord> InPayPalNotInEShop,
    IReadOnlyList<ReconciliationEShopEntry> InEShopNotInPayPal,
    ReconciliationSummary Summary);

/// <summary>A PayPal transaction that lines up with an eShop order (by invoice id).</summary>
public record ReconciliationMatch(
    int OrderId,
    string InvoiceId,
    decimal EShopCapturedAmount,
    string PayPalTransactionId,
    string PayPalStatus,
    decimal PayPalAmount);

/// <summary>An eShop captured payment that PayPal's report does not (yet) show for the range.</summary>
public record ReconciliationEShopEntry(
    int OrderId,
    string InvoiceId,
    decimal CapturedAmount,
    string Status,
    DateTimeOffset? CapturedAt);

public record ReconciliationSummary(
    int PayPalTransactionCount,
    int EShopCapturedCount,
    int MatchedCount,
    int InPayPalOnlyCount,
    int InEShopOnlyCount);
