using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record ReconciliationMatch(PayPalTransactionRecord PayPal, int? OrderId);
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<PayPalTransactionRecord> PayPalTransactions,
    IReadOnlyList<EShopPaymentRecord> EShopPayments,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<PayPalTransactionRecord> PayPalOnly,
    IReadOnlyList<EShopPaymentRecord> EShopOnly);

public record EShopPaymentRecord(
    int OrderId,
    string BuyerId,
    string PaymentStatus,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? CaptureId,
    decimal Total,
    decimal? CapturedAmount,
    DateTimeOffset OrderDate);

public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
