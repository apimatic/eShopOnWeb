using Microsoft.eShopWeb.PublicApi.PaymentModels;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>Request to save (vault) a card for the signed-in shopper.</summary>
public class SavePaymentMethodRequest : BaseRequest
{
    /// <summary>The card to save. Full details are used only to vault the card, never stored.</summary>
    public CardDto? Card { get; set; }

    /// <summary>Owning shopper, set server-side from the bearer token.</summary>
    public string? BuyerId { get; private set; }

    public void SetBuyer(string buyerId) => BuyerId = buyerId;
}
