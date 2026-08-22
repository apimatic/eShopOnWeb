using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationMatch> Matches,
    IReadOnlyList<ReconciliationPayPalOnly> PayPalOnly,
    IReadOnlyList<ReconciliationEshopOnly> EshopOnly);

public sealed record ReconciliationMatch(
    int OrderId,
    string? PayPalTransactionId,
    string? InvoiceId,
    string? EshopPaymentStatus,
    string? PayPalTransactionStatus,
    decimal? PayPalAmount,
    decimal? EshopCapturedAmount);

public sealed record ReconciliationPayPalOnly(
    string PayPalTransactionId,
    string? InvoiceId,
    string? CustomField,
    string? Status,
    decimal? Amount);

public sealed record ReconciliationEshopOnly(
    int OrderId,
    string Status,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? CaptureId,
    decimal Total);
