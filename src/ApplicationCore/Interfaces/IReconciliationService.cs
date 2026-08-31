using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record ReconciliationRow(
    string TransactionId,
    string? PayPalReferenceId,
    string? ReferenceIdType,
    string? InvoiceId,
    string? CustomField,
    string? EventCode,
    DateTimeOffset? Time,
    decimal? Amount,
    string? Currency,
    decimal? Fee,
    string? Status,
    int? MatchedOrderId,
    int? MatchedPaymentId,
    string MatchState);

public record UnmatchedLocalPayment(
    int PaymentId,
    int OrderId,
    string BuyerId,
    decimal Amount,
    string Currency,
    string Status,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? CaptureId,
    DateTimeOffset CreatedAt,
    string MatchState);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationRow> Transactions,
    IReadOnlyList<UnmatchedLocalPayment> UnmatchedLocalPayments);

/// <summary>
/// Lines up the provider's own record of transactions against local orders/payments so a
/// transaction only one side knows about is visible.
/// </summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> GetReportAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
