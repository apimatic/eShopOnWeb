namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// How a shopper wants to pay for an order: either raw card details for a one-off payment,
/// or the id of one of their previously saved cards. Exactly one must be provided.
/// </summary>
public class PaymentInstruction
{
    private PaymentInstruction() { }

    public CardPaymentDetails? Card { get; private init; }

    public int? SavedPaymentMethodId { get; private init; }

    public static PaymentInstruction WithNewCard(CardPaymentDetails card) => new() { Card = card };

    public static PaymentInstruction WithSavedCard(int paymentMethodId) => new() { SavedPaymentMethodId = paymentMethodId };
}
