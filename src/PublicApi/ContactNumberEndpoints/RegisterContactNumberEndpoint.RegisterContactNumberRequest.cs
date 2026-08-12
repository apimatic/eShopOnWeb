using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class RegisterContactNumberRequest : BaseRequest
{
    /// <summary>The mobile number to register. Whatever the caller types; it is validated and canonicalised by the provider.</summary>
    [Required]
    public string PhoneNumber { get; set; } = string.Empty;
}
