using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class RegisterContactNumberRequest : BaseRequest
{
    /// <summary>The mobile number to register, in E.164 or national format.</summary>
    [Required]
    public string PhoneNumber { get; set; } = string.Empty;
}
