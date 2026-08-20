using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<PayPalReportedTransaction> PayPalOnly,
    IReadOnlyList<EShopPaymentRecord> EShopOnly);

public sealed record ReconciliationMatch(
    PayPalReportedTransaction PayPal,
    EShopPaymentRecord EShop);

public sealed record EShopPaymentRecord(
    int OrderId,
    string Status,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? CaptureId,
    IReadOnlyList<string> RefundIds,
    decimal Total,
    string? Currency,
    DateTimeOffset OrderDate);
