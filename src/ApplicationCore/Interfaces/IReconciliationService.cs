using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record ReconciliationRow(
    string MatchStatus,
    int? OrderId,
    string? PayPalTransactionId,
    string? EshopPaymentId,
    string? InvoiceId,
    decimal? PayPalAmount,
    decimal? EshopAmount,
    string? Currency,
    string? PayPalStatus,
    string? EshopStatus,
    DateTimeOffset? PayPalInitiatedAt);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int EshopPaymentCount,
    int MatchedCount,
    int PayPalOnlyCount,
    int EshopOnlyCount,
    IReadOnlyList<ReconciliationRow> Rows);

public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
