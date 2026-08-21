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

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<ReconciliationPayPalOnly> PayPalOnly,
    IReadOnlyList<ReconciliationEshopOnly> EshopOnly);

public record ReconciliationMatch(
    int OrderId,
    string? PayPalTransactionId,
    string? InvoiceId,
    string? CustomField,
    string? PayPalStatus,
    string? PayPalAmount,
    decimal? EshopTotal,
    string? EshopPaymentStatus);

public record ReconciliationPayPalOnly(
    string? PayPalTransactionId,
    string? InvoiceId,
    string? CustomField,
    string? Status,
    string? Amount,
    string? FeeAmount);

public record ReconciliationEshopOnly(
    int OrderId,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? CaptureId,
    decimal Total,
    string PaymentStatus);
