using System;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints.ContactNumbers;

public class CreateContactNumberResponse : BaseResponse
{
    public CreateContactNumberResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateContactNumberResponse()
    {
    }

    /// <summary>The identifier of the number just registered.</summary>
    public int ContactNumberId { get; set; }

    /// <summary>The provider's canonical form of the number that was stored.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}
