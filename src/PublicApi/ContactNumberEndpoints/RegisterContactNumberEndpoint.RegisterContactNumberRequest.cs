namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class RegisterContactNumberRequest : BaseRequest
{
    /// <summary>The mobile number to register, in whatever form the shopper typed it.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}
