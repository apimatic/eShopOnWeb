using Microsoft.eShopWeb.PublicApi.PaymentModels;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SavePaymentMethodRequest : BaseRequest
{
    /// <summary>The card to save. Full details go to PayPal's vault only; never stored locally.</summary>
    public CardRequestModel Card { get; set; } = new();

    /// <summary>Optional friendly name for the saved card.</summary>
    public string? Alias { get; set; }
}
