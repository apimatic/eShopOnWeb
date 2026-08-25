namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodRequest : BaseRequest
{
    /// <summary>Set from the caller's JWT identity — never trust a client-supplied value.</summary>
    public string BuyerId { get; set; } = string.Empty;

    public CardDetailsDto Card { get; set; } = null!;
}
