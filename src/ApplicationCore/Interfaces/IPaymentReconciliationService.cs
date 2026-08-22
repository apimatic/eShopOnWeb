using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payment;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<PayPalReportedTransaction> PayPalTransactions,
    IReadOnlyList<ReconciledMatch> Matches,
    IReadOnlyList<PayPalReportedTransaction> PayPalOnly,
    IReadOnlyList<EshopPaymentRecord> EshopOnly);

public record ReconciledMatch(int OrderId, PayPalReportedTransaction Transaction);

public record EshopPaymentRecord(
    int OrderId,
    string Status,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? CaptureId,
    IReadOnlyList<string> RefundIds);
