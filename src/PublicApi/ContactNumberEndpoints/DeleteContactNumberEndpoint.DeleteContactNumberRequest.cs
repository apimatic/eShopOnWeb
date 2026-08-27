using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class DeleteContactNumberRequest : BaseRequest
{
    public DeleteContactNumberRequest(int contactNumberId)
    {
        ContactNumberId = contactNumberId;
    }

    public int ContactNumberId { get; }

    /// <summary>Set from the JWT, never from the request.</summary>
    [JsonIgnore]
    public string OwnerId { get; set; } = string.Empty;
}
