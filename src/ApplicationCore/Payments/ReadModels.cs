using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>An eShop order paired with its payment state, for the caller's order list.</summary>
public record OrderWithPayment(Order Order, Payment Payment);

/// <summary>One line of the reconciliation report: a PayPal transaction lined up against an eShop payment.</summary>
public record ReconciliationLine(
    string Disposition,          // "Matched", "PayPalOnly", "EShopOnly"
    string? InvoiceReference,
    int? OrderId,
    string? PayPalTransactionId,
    string? PayPalStatus,
    decimal? PayPalAmount,
    string? EShopPaymentStatus,
    decimal? EShopAmount);

/// <summary>The reconciliation report over a date range.</summary>
public record ReconciliationReport(
    System.DateTimeOffset From,
    System.DateTimeOffset To,
    int PayPalTransactionCount,
    int MatchedCount,
    int PayPalOnlyCount,
    int EShopOnlyCount,
    IReadOnlyList<ReconciliationLine> Lines);
