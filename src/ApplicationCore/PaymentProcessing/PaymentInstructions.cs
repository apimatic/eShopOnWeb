using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

/// <summary>One line of an order: a catalog item and how many of it.</summary>
public sealed record OrderLine(int CatalogItemId, int Quantity);

/// <summary>
/// How to pay for an order: either raw <see cref="Card"/> details for a one-off payment, or the id
/// of one of the shopper's <see cref="SavedPaymentMethodId"/> saved cards. Exactly one is required.
/// </summary>
public sealed class PayInstruction
{
    public CardDetails? Card { get; init; }
    public int? SavedPaymentMethodId { get; init; }
}

/// <summary>A reconciliation report lining PayPal's records up against eShop's own payments.</summary>
public sealed class ReconciliationReport
{
    public string From { get; init; } = default!;
    public string To { get; init; } = default!;

    /// <summary>Rows PayPal reported that matched an eShop payment.</summary>
    public List<ReconciliationRow> Matched { get; init; } = new();

    /// <summary>Transactions PayPal knows about that eShop has no payment for.</summary>
    public List<ReconciliationRow> InPayPalOnly { get; init; } = new();

    /// <summary>eShop payment references with no matching PayPal transaction in the range.</summary>
    public List<ReconciliationRow> InEShopOnly { get; init; } = new();

    public int PayPalTransactionCount { get; init; }
    public int EShopReferenceCount { get; init; }
}

public sealed record ReconciliationRow
{
    public string? PayPalTransactionId { get; init; }
    public string? PayPalStatus { get; init; }
    public decimal? PayPalAmount { get; init; }
    public string? Currency { get; init; }
    public int? OrderId { get; init; }
    /// <summary>What the eShop reference is: AUTHORIZATION, CAPTURE or REFUND.</summary>
    public string? EShopReferenceType { get; init; }
    public string? EShopReferenceId { get; init; }
}
