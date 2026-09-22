namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// How to pay for an order: either raw card details for a one-off payment, or one of the shopper's
/// saved cards by id. Exactly one must be supplied.
/// </summary>
public record PayInstruction
{
    public CardDetails? Card { get; init; }
    public int? SavedPaymentMethodId { get; init; }
}
