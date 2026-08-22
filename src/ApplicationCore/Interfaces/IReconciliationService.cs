using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}

public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<ProviderTransaction> PayPalOnly,
    IReadOnlyList<ReconciliationOrder> EshopOnly);

public sealed record ReconciliationMatch(
    int OrderId,
    string? PayPalTransactionId,
    string? InvoiceId,
    string OrderStatus,
    string? PayPalStatus,
    decimal OrderTotal,
    string? TransactionAmount);

public sealed record ReconciliationOrder(
    int OrderId,
    string Status,
    decimal Total,
    string? PayPalOrderId,
    string? PayPalCaptureId,
    string? PayPalAuthorizationId);
