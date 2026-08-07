using Microsoft.eShopWeb.PublicApi.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodRequest : BaseRequest
{
    /// <summary>The card to save. Passed to PayPal's vault; never stored or logged by this app.</summary>
    public CardDto Card { get; set; } = new();

    /// <summary>Optional nickname to help the shopper recognise the card later.</summary>
    public string? Label { get; set; }
}
