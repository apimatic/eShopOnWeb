using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class CreateContactNumberRequest : BaseRequest
{
    [Required]
    public string PhoneNumber { get; set; } = string.Empty;
}
