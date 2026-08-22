using System;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class DeleteContactNumberResponse : BaseResponse
{
    public DeleteContactNumberResponse(Guid correlationId) : base(correlationId)
    {
    }

    public string Status { get; set; } = "Deleted";
}
