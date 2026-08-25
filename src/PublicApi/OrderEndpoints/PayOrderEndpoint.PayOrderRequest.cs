namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    /// <summary>Set by the endpoint from the route - any client-supplied value is ignored.</summary>
    public int OrderId { get; set; }

    /// <summary>Set by the endpoint from the caller's JWT identity - any client-supplied value is ignored.</summary>
    public string BuyerId { get; set; } = string.Empty;

    /// <summary>Exactly one of Card or SavedPaymentMethodId must be supplied.</summary>
    public CardDetailsRequest? Card { get; set; }

    public int? SavedPaymentMethodId { get; set; }
}
