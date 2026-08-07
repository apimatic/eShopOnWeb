using Microsoft.eShopWeb.PublicApi.PaymentModels;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>Saves (vaults) a card for the signed-in shopper for reuse on later orders.</summary>
public class CreatePaymentMethodRequest : BaseRequest
{
    public CardInput Card { get; set; } = new();
}
