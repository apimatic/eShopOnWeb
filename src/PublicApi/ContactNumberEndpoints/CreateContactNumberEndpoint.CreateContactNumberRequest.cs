namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class CreateContactNumberRequest : BaseRequest
{
    /// <summary>The mobile number, in E.164 or national format.</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>Two-letter ISO country code, used when the number is in national format.</summary>
    public string? CountryCode { get; set; }
}
