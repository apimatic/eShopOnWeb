using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class CreateContactNumberRequest : BaseRequest
{
    [Required]
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>
    /// Populated from the caller's token; never taken from the request body.
    /// </summary>
    [JsonIgnore]
    public string OwnerId { get; set; } = string.Empty;
}
