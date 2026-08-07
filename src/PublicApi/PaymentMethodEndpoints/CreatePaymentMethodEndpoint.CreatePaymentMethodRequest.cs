using Microsoft.eShopWeb.PublicApi.PaymentModels;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodRequest : BaseRequest
{
    /// <summary>The card to save. Full details are sent to PayPal for vaulting and never stored locally.</summary>
    public CardRequest? Card { get; set; }

    /// <summary>Optional friendly label for the saved card.</summary>
    public string? Alias { get; set; }
}
