using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record ReconciliationRow(
    string MatchStatus,
    int? OrderId,
    string? EshopPaymentStatus,
    string? PayPalTransactionId,
    string? PayPalReferenceId,
    string? InvoiceId,
    string? Amount,
    string? Currency,
    DateTimeOffset? PayPalTransactionDate);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationRow> Items,
    int PaypalTransactionCount,
    int EshopOrderCount,
    int MatchedCount,
    int PaypalOnlyCount,
    int EshopOnlyCount);

public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
