namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class DeleteContactNumberRequest
{
    public DeleteContactNumberRequest(int contactNumberId, string buyerId)
    {
        ContactNumberId = contactNumberId;
        BuyerId = buyerId;
    }

    public int ContactNumberId { get; }
    public string BuyerId { get; }
}
