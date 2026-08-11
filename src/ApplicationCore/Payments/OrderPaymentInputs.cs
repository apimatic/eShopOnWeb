namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>A single line of an order placement request: a catalog item and how many.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>
/// How to pay: either raw <see cref="CardDetails"/> for a one-off payment, or the id of one of the
/// shopper's saved cards. Exactly one must be provided.
/// </summary>
public record PaymentInstruction(CardDetails? Card, int? SavedCardId)
{
    public bool UsesSavedCard => SavedCardId.HasValue;
    public bool IsValid => (Card is null) ^ (SavedCardId is null);
}
