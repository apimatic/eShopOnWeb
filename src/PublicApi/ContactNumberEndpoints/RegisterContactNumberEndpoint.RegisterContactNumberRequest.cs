namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class RegisterContactNumberRequest : BaseRequest
{
    /// <summary>The mobile number as the caller typed it. It is validated and canonicalized by the provider
    /// before anything is stored.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}
