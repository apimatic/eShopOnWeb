using System;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class RegisterContactNumberResponse : BaseResponse
{
    public RegisterContactNumberResponse(Guid correlationId) : base(correlationId)
    {
    }

    public RegisterContactNumberResponse()
    {
    }

    public int ContactNumberId { get; set; }
    public string CanonicalNumber { get; set; } = string.Empty;
}
