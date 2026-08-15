using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Payments;

/// <summary>A catalog line requested when placing an order.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>
/// How to pay for an order: either raw card details for a one-off payment, or the id of one of the
/// shopper's saved cards. Exactly one must be supplied.
/// </summary>
public class PayOrderCommand
{
    public PayPalCardDetails? Card { get; set; }
    public int? PaymentMethodId { get; set; }
}

/// <summary>Outcome of a reconciliation run for a date range.</summary>
public record ReconciliationReport(
    System.DateTimeOffset From,
    System.DateTimeOffset To,
    IReadOnlyList<ReconciliationLine> Lines,
    int MatchedCount,
    int PayPalOnlyCount,
    int EShopOnlyCount);

/// <summary>
/// A single reconciled row lining up a PayPal transaction against an eShop order (either side may
/// be missing, which is exactly what the report is meant to surface).
/// </summary>
public record ReconciliationLine(
    string Kind,                 // "Matched", "PayPalOnly", or "EShopOnly"
    string? PayPalTransactionId,
    string? PayPalStatus,
    decimal? PayPalAmount,
    string? Currency,
    string? InvoiceId,
    int? OrderId,
    string? OrderStatus,
    decimal? OrderCapturedAmount);
