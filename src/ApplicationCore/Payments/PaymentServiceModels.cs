using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>A catalog item + quantity for placing an order.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>
/// How a shopper wants to pay: either raw card details for a one-off payment, or the id of one of their saved
/// cards. Exactly one must be provided.
/// </summary>
public record PaymentInstrument
{
    public CardDetails? Card { get; init; }
    public int? SavedPaymentMethodId { get; init; }
}

/// <summary>The comparison of a single transaction between PayPal's records and eShop's, for reconciliation.</summary>
public enum ReconciliationMatch
{
    /// <summary>Present on both sides.</summary>
    Matched,
    /// <summary>PayPal knows about it, eShop does not.</summary>
    MissingInEShop,
    /// <summary>eShop knows about it, PayPal's records do not (may be reporting lag).</summary>
    MissingInPayPal
}

/// <summary>One line of the reconciliation report.</summary>
public record ReconciliationLine
{
    public required ReconciliationMatch Match { get; init; }
    public int? OrderId { get; init; }
    public string? InvoiceId { get; init; }
    public string? PayPalTransactionId { get; init; }
    public string? EventCode { get; init; }
    public string? PayPalStatus { get; init; }
    public decimal? PayPalAmount { get; init; }
    public decimal? EShopAmount { get; init; }
    public string? EShopPaymentStatus { get; init; }
    public DateTimeOffset? Date { get; init; }
    public string? Note { get; init; }
}

/// <summary>The reconciliation report over a date range: PayPal's transactions lined up against eShop orders.</summary>
public record ReconciliationReport
{
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }
    public required int PayPalTransactionCount { get; init; }
    public required int EShopPaymentCount { get; init; }
    public required int MatchedCount { get; init; }
    public required int MissingInEShopCount { get; init; }
    public required int MissingInPayPalCount { get; init; }
    public required IReadOnlyList<ReconciliationLine> Lines { get; init; }
}
