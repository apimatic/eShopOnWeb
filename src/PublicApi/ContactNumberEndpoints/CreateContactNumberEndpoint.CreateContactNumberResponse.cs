using System;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class CreateContactNumberResponse : BaseResponse
{
    public CreateContactNumberResponse(Guid correlationId) : base(correlationId) { }

    public CreateContactNumberResponse() { }

    /// <summary>Top-level identifier of the registered number, so the caller can act on it later.</summary>
    public int ContactNumberId { get; set; }

    /// <summary>The provider's canonical E.164 form that was actually stored.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}
