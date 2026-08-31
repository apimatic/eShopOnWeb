namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class CreateContactNumberRequest : BaseRequest
{
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>Populated from the caller's token, never from request input.</summary>
    public string? BuyerId { get; set; }
}
