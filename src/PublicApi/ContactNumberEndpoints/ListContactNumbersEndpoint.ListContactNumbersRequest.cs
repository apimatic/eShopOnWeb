namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class ListContactNumbersRequest : CancellableRequest
{
    public string? BuyerId { get; set; }
}
