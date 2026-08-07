using Microsoft.eShopWeb.PublicApi.PaymentModels;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodRequest : BaseRequest
{
    /// <summary>The card to save. Full details are vaulted at PayPal, never stored by the application.</summary>
    public CardRequest Card { get; set; } = new();

    /// <summary>Optional friendly label. Defaults to "{brand} ****{last4}" when omitted.</summary>
    public string? Alias { get; set; }
}
