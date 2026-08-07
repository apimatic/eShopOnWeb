using Microsoft.eShopWeb.PublicApi.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>Saves a card for the signed-in shopper. The card is vaulted at PayPal; only PCI-safe display
/// data is kept in this application.</summary>
public class CreatePaymentMethodRequest : BaseRequest
{
    public CardRequest Card { get; set; } = new();

    /// <summary>Optional nickname to help the shopper recognise the card.</summary>
    public string? Alias { get; set; }
}
