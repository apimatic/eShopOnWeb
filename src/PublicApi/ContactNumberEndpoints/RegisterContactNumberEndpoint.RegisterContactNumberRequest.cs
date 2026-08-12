namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class RegisterContactNumberRequest : BaseRequest
{
    /// <summary>The mobile number to register, in any form the provider can canonicalise.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}
