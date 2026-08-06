namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>Request to remove a saved card. Id and buyer come from the route/token.</summary>
public class DeletePaymentMethodRequest : BaseRequest
{
    public int PaymentMethodId { get; private set; }
    public string? BuyerId { get; private set; }

    public void SetRouteAndBuyer(int paymentMethodId, string buyerId)
    {
        PaymentMethodId = paymentMethodId;
        BuyerId = buyerId;
    }
}
