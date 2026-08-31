namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class DeleteContactNumberRequest : CancellableRequest
{
    public DeleteContactNumberRequest(int contactNumberId)
    {
        ContactNumberId = contactNumberId;
    }

    public int ContactNumberId { get; init; }
    public string? BuyerId { get; set; }
}
