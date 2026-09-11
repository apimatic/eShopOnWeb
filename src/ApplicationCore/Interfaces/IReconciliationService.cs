using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Builds a reconciliation report over a date range: PayPal's own record of transactions lined up
/// against eShop orders, so a payment PayPal knows about but eShop doesn't (or the reverse) is visible.
/// </summary>
public interface IReconciliationService
{
    Task<Result<ReconciliationReport>> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<ReconciliationPayPalOnly> InPayPalNotEShop,
    IReadOnlyList<ReconciliationEShopOnly> InEShopNotPayPal)
{
    public int PayPalTransactionCount => Matched.Count + InPayPalNotEShop.Count;
}

/// <summary>A PayPal transaction that lines up with an eShop payment (by invoice/order correlation).</summary>
public record ReconciliationMatch(
    int OrderId,
    string EShopPaymentStatus,
    string PayPalTransactionId,
    string? InvoiceId,
    decimal PayPalAmount,
    string? PayPalStatus,
    string? EventCode,
    DateTimeOffset? InitiationDate);

/// <summary>A PayPal transaction with no matching eShop order in this store.</summary>
public record ReconciliationPayPalOnly(
    string PayPalTransactionId,
    string? InvoiceId,
    decimal PayPalAmount,
    string? CurrencyCode,
    string? PayPalStatus,
    string? EventCode,
    DateTimeOffset? InitiationDate);

/// <summary>An eShop payment that PayPal's report does not (yet) show for the range.</summary>
public record ReconciliationEShopOnly(
    int OrderId,
    string EShopPaymentStatus,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? CaptureId,
    decimal Amount,
    string CurrencyCode);
