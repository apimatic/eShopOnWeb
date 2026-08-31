namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class CreateContactNumberRequest : BaseRequest
{
    /// <summary>The mobile number as the shopper typed it.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}
