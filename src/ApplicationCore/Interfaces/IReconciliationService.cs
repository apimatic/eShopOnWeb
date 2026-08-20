using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(string from, string to, CancellationToken cancellationToken);
}

public sealed record ReconciliationReport(
    string From,
    string To,
    IReadOnlyList<ReconciliationRow> Rows,
    int PayPalTransactionCount,
    int EshopOrderCount,
    int MatchedCount,
    int PayPalOnlyCount,
    int EshopOnlyCount);

public sealed record ReconciliationRow(
    string MatchStatus,
    int? OrderId,
    string? OrderPaymentStatus,
    string? PayPalTransactionId,
    string? PayPalStatus,
    string? InvoiceId,
    string? CustomField,
    PayPalMoneyDto? PayPalAmount,
    decimal? OrderTotal);
