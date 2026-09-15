using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record ReconciliationRow(
    string Source,
    string? PaypalTransactionId,
    int? OrderId,
    string? Match,
    string? PaypalStatus,
    string? OrderStatus,
    decimal? PaypalAmount,
    decimal? OrderAmount,
    string? Currency,
    DateTimeOffset? PaypalDate);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PaypalTransactionCount,
    int EshopOrderCount,
    IReadOnlyList<ReconciliationRow> Rows);

public interface IPaymentReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to);
}
