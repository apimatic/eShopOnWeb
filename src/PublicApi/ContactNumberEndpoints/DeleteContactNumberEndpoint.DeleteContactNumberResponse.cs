using System;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class DeleteContactNumberResponse : BaseResponse
{
    public DeleteContactNumberResponse(Guid correlationId) : base(correlationId) {}
    public DeleteContactNumberResponse() {}

    public bool Deleted { get; set; }
}
