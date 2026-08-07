namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// How to pay an order: either with a one-off <see cref="Card"/> or with one of the shopper's
/// <see cref="SavedPaymentMethodId"/> saved cards. Exactly one must be provided.
/// </summary>
public sealed record PaymentInstruction
{
    public CardDetails? Card { get; init; }
    public int? SavedPaymentMethodId { get; init; }

    public bool UsesSavedCard => SavedPaymentMethodId.HasValue;
}
