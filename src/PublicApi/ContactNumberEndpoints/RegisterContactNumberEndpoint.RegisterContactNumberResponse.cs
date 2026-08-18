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

    /// <summary>Identifier of the number just registered.</summary>
    public int ContactNumberId { get; set; }

    public ContactNumberDto ContactNumber { get; set; } = new();
}
