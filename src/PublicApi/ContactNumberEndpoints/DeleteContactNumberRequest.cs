namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class DeleteContactNumberRequest : BaseRequest
{
    public int ContactNumberId { get; init; }

    public DeleteContactNumberRequest(int contactNumberId)
    {
        ContactNumberId = contactNumberId;
    }
}

public class DeleteContactNumberResponse : BaseResponse
{
    public DeleteContactNumberResponse(System.Guid correlationId) : base(correlationId)
    {
    }

    public DeleteContactNumberResponse()
    {
    }
}
