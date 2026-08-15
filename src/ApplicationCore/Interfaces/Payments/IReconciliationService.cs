using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>
/// Operator report that lines PayPal's own transaction records up against eShop payments over a date
/// range, so a transaction PayPal knows about that eShop doesn't — or the reverse — is visible.
/// </summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}

/// <summary>How a single transaction lines up between the two systems.</summary>
public enum ReconciliationState
{
    /// <summary>Present in both PayPal and eShop.</summary>
    Matched = 0,

    /// <summary>PayPal has a transaction eShop has no record of.</summary>
    MissingInEShop = 1,

    /// <summary>eShop expected a PayPal transaction that the report does not (yet) show.</summary>
    MissingInPayPal = 2
}

public sealed record ReconciliationLine(
    ReconciliationState State,
    string? PayPalTransactionId,
    string? PayPalStatus,
    decimal? PayPalAmount,
    string? CurrencyCode,
    DateTimeOffset? PayPalDate,
    int? OrderId,
    string? EShopReference,
    string? EShopKind);

public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int EShopReferenceCount,
    int MatchedCount,
    int MissingInEShopCount,
    int MissingInPayPalCount,
    IReadOnlyList<ReconciliationLine> Lines);
