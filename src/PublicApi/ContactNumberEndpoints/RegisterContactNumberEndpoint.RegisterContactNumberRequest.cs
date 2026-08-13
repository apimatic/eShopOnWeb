namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class RegisterContactNumberRequest : BaseRequest
{
    /// <summary>The mobile number as typed by the caller. It is validated and canonicalised before storage.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}
