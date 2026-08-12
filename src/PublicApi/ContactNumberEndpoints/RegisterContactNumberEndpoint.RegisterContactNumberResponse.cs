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

    /// <summary>The identifier of the newly registered number (top-level, so callers can drive flows).</summary>
    public int ContactNumberId { get; set; }

    public string PhoneNumber { get; set; } = string.Empty;
}
