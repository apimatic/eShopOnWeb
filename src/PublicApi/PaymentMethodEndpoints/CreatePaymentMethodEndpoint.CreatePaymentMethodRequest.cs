namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodRequest : BaseRequest
{
    public CreatePaymentMethodRequest(string buyerId, CardDetailsDto card)
    {
        BuyerId = buyerId;
        Card = card;
    }

    public string BuyerId { get; }
    public CardDetailsDto Card { get; }
}
