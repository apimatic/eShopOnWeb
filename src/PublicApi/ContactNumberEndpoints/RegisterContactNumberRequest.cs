namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class RegisterContactNumberRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
    internal string BuyerId { get; set; } = string.Empty;
}
