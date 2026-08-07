namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodResponse
{
    /// <summary>The new saved card's id (top-level, so a caller can pay with it or delete it).</summary>
    public int PaymentMethodId { get; set; }

    public SavedCardDto? PaymentMethod { get; set; }
}
