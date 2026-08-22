namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class CreateContactNumberRequest : BaseRequest
{
    public string PhoneNumber { get; set; } = string.Empty;

    internal string BuyerId { get; set; } = string.Empty;
    internal System.Threading.CancellationToken CancellationToken { get; set; }

}
