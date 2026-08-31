namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class CreateContactNumberResponse : BaseResponse
{
    public int ContactNumberId { get; set; }

    /// <summary>The provider's canonical form of the registered number.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}
