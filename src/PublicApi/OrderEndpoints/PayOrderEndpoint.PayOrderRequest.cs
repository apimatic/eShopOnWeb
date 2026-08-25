namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    /// <summary>Folded in from the route by the endpoint, not bound from the body.</summary>
    public int OrderId { get; set; }

    /// <summary>Set from the caller's JWT identity — never trust a client-supplied value.</summary>
    public string BuyerId { get; set; } = string.Empty;

    /// <summary>Supply this OR <see cref="SavedPaymentMethodId"/>, never both.</summary>
    public CardDetailsDto? Card { get; set; }

    public int? SavedPaymentMethodId { get; set; }
}
