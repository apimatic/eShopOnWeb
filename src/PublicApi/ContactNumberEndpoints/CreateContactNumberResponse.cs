using System;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class CreateContactNumberResponse : BaseResponse
{
    public CreateContactNumberResponse(Guid correlationId) : base(correlationId)
    {
    }

    public int ContactNumberId { get; set; }
    public string CanonicalNumber { get; set; } = string.Empty;
}
