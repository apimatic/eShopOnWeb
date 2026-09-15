namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>A contact number as returned to its owner.</summary>
public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
}
