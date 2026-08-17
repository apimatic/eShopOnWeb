using System;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class RegisterContactNumberResponse : BaseResponse
{
    public RegisterContactNumberResponse(Guid correlationId) : base(correlationId) { }

    public RegisterContactNumberResponse() { }

    /// <summary>Identifier of the registered number, returned as a top-level field so callers can act on it.</summary>
    public int ContactNumberId { get; set; }

    /// <summary>The provider's canonical E.164 form of the number that was stored.</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    public DateTimeOffset RegisteredAt { get; set; }
}
