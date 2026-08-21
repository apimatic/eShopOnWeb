namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    /// <summary>Raw card details for a one-off payment. Provide this OR <see cref="PaymentMethodId"/>.</summary>
    public CardRequestDto? Card { get; set; }

    /// <summary>Id of one of the shopper's saved cards to pay with. Provide this OR <see cref="Card"/>.</summary>
    public int? PaymentMethodId { get; set; }

    // Set from the route / token; never bound from the request body.
    public int OrderId { get; set; }
    public string? BuyerId { get; set; }
}
