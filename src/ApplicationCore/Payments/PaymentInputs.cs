using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>A requested order line: a catalog item and how many of it.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>
/// How to pay: either raw card details for a one-off payment, or the id of one of the shopper's saved cards.
/// Exactly one of the two must be supplied.
/// </summary>
public record PaymentInstruction(CardDetails? Card, int? SavedPaymentMethodId)
{
    public bool IsValid => (Card is null) ^ (SavedPaymentMethodId is null);
}

/// <summary>A single reconciliation row: PayPal's record for a transaction lined up against an eShop order.</summary>
public record ReconciliationLine(
    string PayPalTransactionId,
    decimal Amount,
    string CurrencyCode,
    string PayPalStatus,
    System.DateTimeOffset Date,
    int? OrderId,
    string MatchState);

public record ReconciliationReport(
    System.DateTimeOffset From,
    System.DateTimeOffset To,
    IReadOnlyList<ReconciliationLine> Lines);
