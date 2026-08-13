namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class RegisterContactNumberRequest
{
    /// <summary>The mobile number to register, in any form the provider can canonicalise to E.164.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}
