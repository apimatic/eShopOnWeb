namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class CreateContactNumberRequest : BaseRequest
{
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>Optional ISO country code used when the number is supplied in national format.</summary>
    public string? CountryCode { get; set; }
}
