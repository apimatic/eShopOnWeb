namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class RegisterContactNumberRequest : BaseRequest
{
    /// <summary>The mobile number to register, in any format the provider can parse.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}
