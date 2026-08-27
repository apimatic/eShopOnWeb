namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class CreateContactNumberRequest : BaseRequest
{
    /// <summary>The raw number as the shopper typed it (E.164 or national format).</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>Two-letter ISO country code, required when PhoneNumber is in national format.</summary>
    public string? CountryCode { get; set; }
}
