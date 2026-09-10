namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// How a shopper chooses to pay for an order: either raw card details for a one-off payment, or one
/// of their saved cards named by its local id. Exactly one must be supplied.
/// </summary>
public record PaymentInstrument(CardDetails? Card, int? SavedPaymentMethodId)
{
    public bool UsesSavedCard => SavedPaymentMethodId.HasValue;
}

/// <summary>A requested order line: a catalog item and how many of it.</summary>
public record OrderLineItem(int CatalogItemId, int Quantity);
