using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Produces PayPal-vs-eShop reconciliation over a date range: every transaction PayPal reports
/// lined up against the eShop order that owns it, so a payment one side knows about and the
/// other does not is visible.
/// </summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public enum ReconciliationOutcome
{
    /// <summary>PayPal reports the transaction and an eShop order owns it.</summary>
    Matched,

    /// <summary>PayPal reports a transaction no eShop order accounts for.</summary>
    InPayPalOnly,

    /// <summary>An eShop payment record PayPal's report does not (yet) show.</summary>
    InEShopOnly
}

public sealed record ReconciliationEntry(
    ReconciliationOutcome Outcome,
    string? PayPalTransactionId,
    string? PayPalStatus,
    decimal? PayPalAmount,
    string? CurrencyCode,
    int? OrderId,
    string? EShopReference,
    string? EShopPaymentStatus);

public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationEntry> Entries)
{
    public int MatchedCount => Entries.Count(e => e.Outcome == ReconciliationOutcome.Matched);
    public int InPayPalOnlyCount => Entries.Count(e => e.Outcome == ReconciliationOutcome.InPayPalOnly);
    public int InEShopOnlyCount => Entries.Count(e => e.Outcome == ReconciliationOutcome.InEShopOnly);
}
