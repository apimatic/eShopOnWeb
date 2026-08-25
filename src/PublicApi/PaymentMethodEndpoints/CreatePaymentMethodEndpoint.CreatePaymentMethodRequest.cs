namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodRequest : BaseRequest
{
    /// <summary>Set by the endpoint from the caller's JWT identity - any client-supplied value is ignored.</summary>
    public string BuyerId { get; set; } = string.Empty;

    public CardDetailsRequest Card { get; set; } = new();
}
