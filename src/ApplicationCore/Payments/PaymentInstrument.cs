namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// How a shopper chooses to pay: either a one-off raw card, or one of their saved cards.
/// Exactly one of <see cref="Card"/> / <see cref="SavedPaymentMethodId"/> is set.
/// </summary>
public record PaymentInstrument
{
    public CardPaymentDetails? Card { get; init; }
    public int? SavedPaymentMethodId { get; init; }

    public static PaymentInstrument FromCard(CardPaymentDetails card) => new() { Card = card };
    public static PaymentInstrument FromSavedCard(int savedPaymentMethodId) => new() { SavedPaymentMethodId = savedPaymentMethodId };
}
