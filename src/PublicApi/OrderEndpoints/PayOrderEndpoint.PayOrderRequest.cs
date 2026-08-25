using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    [FromRoute(Name = "orderId")]
    public int OrderId { get; set; }

    /// <summary>Card details for a one-off payment. Provide this OR <see cref="SavedPaymentMethodId"/>, not both.</summary>
    [FromBody]
    public PayOrderBody Body { get; set; } = new();
}

public class PayOrderBody
{
    public CardDetailsRequest? Card { get; set; }
    public int? SavedPaymentMethodId { get; set; }
}
