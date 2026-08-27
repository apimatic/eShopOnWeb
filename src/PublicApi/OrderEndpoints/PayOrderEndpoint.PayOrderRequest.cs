namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    /// <summary>One-off card details. Mutually exclusive with SavedPaymentMethodId.</summary>
    public CardDetailsRequest? Card { get; set; }

    /// <summary>Id of one of the caller's saved cards. Mutually exclusive with Card.</summary>
    public int? SavedPaymentMethodId { get; set; }
}
