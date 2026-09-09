using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Builds a reconciliation report: PayPal's own record of transactions for a date range,
/// lined up against eShop orders, so a payment one side knows about and the other doesn't
/// is visible.
/// </summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public record ReconciliationReport
{
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }

    /// <summary>PayPal transactions that matched an eShop order (by invoice id).</summary>
    public required IReadOnlyList<ReconciliationMatch> Matched { get; init; }

    /// <summary>Transactions PayPal reported that no eShop order accounts for.</summary>
    public required IReadOnlyList<ReconciliationPayPalEntry> InPayPalNotInEshop { get; init; }

    /// <summary>eShop orders with a captured payment that PayPal's report does not show for this range.</summary>
    public required IReadOnlyList<ReconciliationEshopEntry> InEshopNotInPayPal { get; init; }
}

public record ReconciliationMatch
{
    public required int OrderId { get; init; }
    public required string PayPalTransactionId { get; init; }
    public string? PayPalStatus { get; init; }
    public decimal PayPalAmount { get; init; }
    public decimal? EshopCapturedAmount { get; init; }
    public string? Currency { get; init; }
    public bool AmountsAgree { get; init; }
}

public record ReconciliationPayPalEntry
{
    public required string PayPalTransactionId { get; init; }
    public string? InvoiceId { get; init; }
    public string? Status { get; init; }
    public decimal Amount { get; init; }
    public string? Currency { get; init; }
    public DateTimeOffset? Date { get; init; }
}

public record ReconciliationEshopEntry
{
    public required int OrderId { get; init; }
    public string? CaptureId { get; init; }
    public decimal? CapturedAmount { get; init; }
    public string? Currency { get; init; }
    public string? InvoiceId { get; init; }
}
