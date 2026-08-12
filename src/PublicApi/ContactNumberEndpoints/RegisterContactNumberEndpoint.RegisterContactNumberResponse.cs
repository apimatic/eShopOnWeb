using System;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class RegisterContactNumberResponse : BaseResponse
{
    public RegisterContactNumberResponse(Guid correlationId) : base(correlationId) { }

    public RegisterContactNumberResponse() { }

    /// <summary>The identifier of the number just registered.</summary>
    public int ContactNumberId { get; set; }

    /// <summary>The canonical E.164 form that was stored.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}
