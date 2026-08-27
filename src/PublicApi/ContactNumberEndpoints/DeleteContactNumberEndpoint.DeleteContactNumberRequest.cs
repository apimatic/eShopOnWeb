using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class DeleteContactNumberRequest : BaseRequest
{
    public DeleteContactNumberRequest(int contactNumberId)
    {
        ContactNumberId = contactNumberId;
    }

    public int ContactNumberId { get; }

    [JsonIgnore]
    public string OwnerId { get; set; } = string.Empty;
}

public class DeleteContactNumberResponse : BaseResponse
{
    public DeleteContactNumberResponse(System.Guid correlationId) : base(correlationId) { }
    public DeleteContactNumberResponse() { }

    public string Status { get; set; } = "Deleted";
}
