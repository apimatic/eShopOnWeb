using System;
using Microsoft.eShopWeb.PublicApi;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class DeleteContactNumberResponse : BaseResponse
{
    public DeleteContactNumberResponse(Guid correlationId) : base(correlationId)
    {
    }

    public DeleteContactNumberResponse()
    {
    }

    public string Status { get; set; } = "Deleted";
}
