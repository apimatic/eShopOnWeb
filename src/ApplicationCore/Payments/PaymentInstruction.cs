namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// How a shopper wants to pay: either raw card details for a one-off payment, or the id of one of
/// their saved cards. Exactly one of the two must be supplied.
/// </summary>
public class PaymentInstruction
{
    public CardDetails? Card { get; init; }

    public int? SavedPaymentMethodId { get; init; }

    public bool UsesSavedCard => SavedPaymentMethodId.HasValue;
}

/// <summary>A requested order line: a catalog item and how many of it.</summary>
public record OrderLine(int CatalogItemId, int Quantity);
