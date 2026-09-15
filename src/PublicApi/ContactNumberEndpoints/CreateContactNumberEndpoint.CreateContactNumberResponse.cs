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

    /// <summary>The identifier of the registered number (top-level, so the flow can be driven by a caller).</summary>
    public int ContactNumberId { get; set; }

    /// <summary>The provider's canonical (E.164) form of the number that was stored.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}
