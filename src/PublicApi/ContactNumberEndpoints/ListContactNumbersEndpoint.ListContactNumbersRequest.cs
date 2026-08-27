using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class ListContactNumbersRequest : BaseRequest
{
    /// <summary>Set from the JWT, never from the request.</summary>
    [JsonIgnore]
    public string OwnerId { get; set; } = string.Empty;
}
