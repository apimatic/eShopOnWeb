using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Lines PayPal's own record of transactions for a date range up against eShop's orders/payments,
/// so a payment PayPal knows about that eShop doesn't — or the reverse — becomes visible.
/// </summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationLine> Matched,
    IReadOnlyList<ReconciliationLine> OnlyInPayPal,
    IReadOnlyList<ReconciliationLine> OnlyInEShop);

public record ReconciliationLine(
    string TransactionId,
    string Kind,
    decimal? Amount,
    string? Currency,
    string? Status,
    int? OrderId,
    DateTimeOffset? Date);
