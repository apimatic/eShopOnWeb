using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class CreateContactNumberRequest : BaseRequest
{
    [Required]
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>Populated from the caller's token; never taken from the request body.</summary>
    public string BuyerId { get; set; } = string.Empty;
}
