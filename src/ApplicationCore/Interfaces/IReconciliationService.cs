using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record ReconciliationRow(
    string MatchStatus,
    int? OrderId,
    string? PayPalTransactionId,
    decimal? EshopAmount,
    decimal? PayPalAmount,
    string? Currency,
    string? PayPalStatus,
    string? OrderPaymentStatus,
    string? Timestamp,
    string? Notes);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationRow> Rows);

public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
