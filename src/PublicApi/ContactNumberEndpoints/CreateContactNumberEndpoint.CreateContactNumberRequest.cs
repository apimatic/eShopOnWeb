using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class CreateContactNumberRequest : CancellableRequest
{
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>Set from the caller's token by the endpoint; never accepted from the request body.</summary>
    [JsonIgnore]
    public string? BuyerId { get; set; }
}
