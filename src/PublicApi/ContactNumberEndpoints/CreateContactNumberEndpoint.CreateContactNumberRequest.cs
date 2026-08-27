using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class CreateContactNumberRequest : BaseRequest
{
    /// <summary>The mobile number as the shopper typed it.</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>Set from the JWT, never from the request body.</summary>
    [JsonIgnore]
    public string OwnerId { get; set; } = string.Empty;
}
