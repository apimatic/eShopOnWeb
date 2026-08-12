namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class RegisterContactNumberRequest : BaseRequest
{
    /// <summary>The mobile number as the shopper typed it (E.164 or national format).</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}
