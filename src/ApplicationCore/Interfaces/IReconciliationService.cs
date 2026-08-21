using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Builds the operator reconciliation report: PayPal's own transaction records for a date range,
/// lined up against eShop orders so a payment one side knows about and the other doesn't is visible.
/// </summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

/// <summary>The reconciliation result for a date range.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<PayPalOnlyEntry> PayPalOnly,
    IReadOnlyList<EShopOnlyEntry> EShopOnly);

/// <summary>A PayPal transaction that lines up with an eShop order (by the stamped order reference).</summary>
public record ReconciliationMatch(
    int OrderId,
    string OrderStatus,
    decimal OrderTotal,
    string PayPalTransactionId,
    string? PayPalStatus,
    decimal? PayPalAmount,
    bool AmountsAgree);

/// <summary>A PayPal transaction with no matching eShop order.</summary>
public record PayPalOnlyEntry(
    string PayPalTransactionId,
    string? PayPalStatus,
    decimal? PayPalAmount,
    string? Currency,
    DateTimeOffset? InitiationDate,
    string? Reference);

/// <summary>An eShop payment PayPal's report does not (yet) show for the range.</summary>
public record EShopOnlyEntry(
    int OrderId,
    string OrderStatus,
    decimal OrderTotal,
    string PayPalOrderId,
    string? CaptureId);
