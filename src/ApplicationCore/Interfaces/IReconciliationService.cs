using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A PayPal transaction that was matched to an eShop order (present in both records).</summary>
public record ReconciliationMatch(
    string PayPalTransactionId,
    int OrderId,
    string? EventCode,
    string? PayPalStatus,
    decimal PayPalAmount,
    decimal EShopAmount,
    bool AmountsAgree);

/// <summary>A PayPal transaction with no corresponding eShop order.</summary>
public record PayPalOnlyTransaction(
    string PayPalTransactionId,
    string? EventCode,
    string? Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? InitiatedAt,
    string? CustomField,
    string? InvoiceId);

/// <summary>An eShop payment PayPal's report does not (yet) show for the range.</summary>
public record EShopOnlyPayment(
    int OrderId,
    string? PayPalCaptureId,
    string PaymentStatus,
    decimal? CapturedAmount,
    DateTimeOffset? CapturedAt);

/// <summary>
/// A reconciliation report over a date range: PayPal's own transactions lined up against eShop
/// orders, so a payment one side knows about and the other doesn't is visible. Covers the whole
/// range (all pages of PayPal reporting), not just the first page.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int EShopPaymentCount,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<PayPalOnlyTransaction> PayPalOnly,
    IReadOnlyList<EShopOnlyPayment> EShopOnly);

public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
