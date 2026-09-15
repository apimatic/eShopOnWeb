using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record ReconciliationLine(
    string Match,
    int? OrderId,
    string? OrderStatus,
    string? PayPalTransactionId,
    string? PayPalReferenceId,
    string? InvoiceId,
    string? EventCode,
    string? PayPalStatus,
    decimal? PayPalAmount,
    decimal? PayPalFee,
    string? Currency,
    string? TransactionDate);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int EshopOrderCount,
    int MatchedCount,
    int PayPalOnlyCount,
    int EshopOnlyCount,
    IReadOnlyList<ReconciliationLine> Lines);

public interface IPaymentReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
