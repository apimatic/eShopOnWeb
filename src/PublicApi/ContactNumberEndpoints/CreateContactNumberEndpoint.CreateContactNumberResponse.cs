using System;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class CreateContactNumberResponse : BaseResponse
{
    public CreateContactNumberResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateContactNumberResponse()
    {
    }

    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; }
}
