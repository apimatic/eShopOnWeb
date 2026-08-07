namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>
/// How a shopper wants to pay an order: either raw <see cref="Card"/> details for a one-off payment,
/// or the id of one of their <see cref="SavedPaymentMethodId"/> saved cards. Exactly one must be supplied.
/// </summary>
public class PaymentInstruction
{
    public CardDetails? Card { get; set; }
    public int? SavedPaymentMethodId { get; set; }

    public bool HasCard => Card is not null;
    public bool HasSavedCard => SavedPaymentMethodId.HasValue;

    /// <summary>True when exactly one payment source was supplied.</summary>
    public bool IsValid => HasCard ^ HasSavedCard;
}
