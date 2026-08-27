using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Safe description of a saved card: enough for the shopper to recognise it,
/// never full card details.
/// </summary>
public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? Expiry { get; set; }

    public static PaymentMethodDto FromSavedCard(SavedCard card)
    {
        return new PaymentMethodDto
        {
            PaymentMethodId = card.Id,
            Brand = card.Brand,
            Last4 = card.Last4,
            Expiry = card.Expiry
        };
    }
}
