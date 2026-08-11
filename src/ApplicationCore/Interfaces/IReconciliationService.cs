using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>How a PayPal transaction and an eShop payment line up.</summary>
public enum ReconciliationMatch
{
    /// <summary>PayPal transaction and eShop payment both present for the same invoice.</summary>
    Matched = 0,

    /// <summary>PayPal reports a transaction eShop has no record of.</summary>
    MissingInEShop = 1,

    /// <summary>eShop has a payment PayPal's report does not (yet) show.</summary>
    MissingInPayPal = 2
}

/// <summary>One reconciled line: a PayPal transaction, an eShop payment, or both.</summary>
public record ReconciliationLine(
    ReconciliationMatch Match,
    string? InvoiceId,
    int? OrderId,
    OrderPaymentStatus? EShopStatus,
    decimal? EShopAmount,
    string? PayPalTransactionId,
    string? PayPalEventCode,
    decimal? PayPalAmount,
    string? PayPalStatus,
    DateTimeOffset? PayPalDate);

/// <summary>Reconciliation of PayPal's transaction record against eShop orders over a date range.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int EShopPaymentCount,
    int MatchedCount,
    int MissingInEShopCount,
    int MissingInPayPalCount,
    IReadOnlyList<ReconciliationLine> Lines);

/// <summary>
/// Builds a reconciliation report over a date range, pulling PayPal's own transaction records (fully paged and
/// chunked into the API's supported windows) and lining them up against eShop orders. Operator action.
/// </summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
