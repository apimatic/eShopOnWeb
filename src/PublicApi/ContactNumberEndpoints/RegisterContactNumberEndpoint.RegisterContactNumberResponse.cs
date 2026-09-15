using System;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class RegisterContactNumberResponse : BaseResponse
{
    public RegisterContactNumberResponse(Guid correlationId) : base(correlationId) { }

    public RegisterContactNumberResponse() { }

    /// <summary>Identifier of the registered number, returned as a top-level field so the flow can be driven on.</summary>
    public int ContactNumberId { get; set; }

    /// <summary>The provider's canonical (E.164) form of the number that was stored.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}
