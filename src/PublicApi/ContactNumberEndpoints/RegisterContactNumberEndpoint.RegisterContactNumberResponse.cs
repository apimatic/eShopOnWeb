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

    /// <summary>The identifier of the registered number.</summary>
    public int ContactNumberId { get; set; }

    /// <summary>The provider's canonical (E.164) form of the number, as stored.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}
