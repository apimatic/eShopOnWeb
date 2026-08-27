namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class CreateContactNumberRequest : BaseRequest
{
    /// <summary>The mobile number to register, in any format the provider can parse (E.164 preferred).</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}
