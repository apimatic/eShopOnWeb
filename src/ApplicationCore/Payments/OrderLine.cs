namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>A requested order line: a catalog item and the quantity to buy.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>
/// How to pay for an order: either raw card details for a one-off payment, or the id of
/// one of the shopper's saved cards. Exactly one must be supplied.
/// </summary>
public record PaymentInstrument(CardPaymentDetails? Card, int? SavedPaymentMethodId);
