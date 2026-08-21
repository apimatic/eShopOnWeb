namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class ListContactNumbersRequest
{
    public ListContactNumbersRequest(string buyerId)
    {
        BuyerId = buyerId;
    }

    public string BuyerId { get; }
}
