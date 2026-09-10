using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A catalog item and quantity to place on an order.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>
/// How to pay: either raw card details for a one-off payment, or the id of one of the shopper's
/// saved cards. Exactly one must be supplied.
/// </summary>
public record PayInstruction
{
    public GatewayCardDetails? Card { get; init; }
    public int? SavedCardId { get; init; }
}

/// <summary>An order paired with its payment state (payment may be null before first pay).</summary>
public record OrderPaymentView(Order Order, Payment? Payment);

/// <summary>One line of the reconciliation report.</summary>
public record ReconciliationLine
{
    public required string Kind { get; init; } // "Matched", "PayPalOnly", "EShopOnly"
    public string? InvoiceId { get; init; }
    public int? OrderId { get; init; }

    // eShop side
    public string? EShopPaymentStatus { get; init; }
    public decimal? EShopCapturedAmount { get; init; }
    public string? EShopCaptureId { get; init; }

    // PayPal side
    public string? PayPalTransactionId { get; init; }
    public string? PayPalStatus { get; init; }
    public string? PayPalEventCode { get; init; }
    public decimal? PayPalAmount { get; init; }
    public string? PayPalCurrency { get; init; }
    public DateTimeOffset? PayPalDate { get; init; }
}

public record ReconciliationReport
{
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }
    public required int PayPalTransactionCount { get; init; }
    public required int EShopPaymentCount { get; init; }
    public required int MatchedCount { get; init; }
    public required int PayPalOnlyCount { get; init; }
    public required int EShopOnlyCount { get; init; }
    public required IReadOnlyList<ReconciliationLine> Lines { get; init; }
}
