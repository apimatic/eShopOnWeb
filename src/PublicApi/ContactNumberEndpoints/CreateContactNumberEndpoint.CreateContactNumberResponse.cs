using System;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class CreateContactNumberResponse : BaseResponse
{
    public CreateContactNumberResponse(Guid correlationId) : base(correlationId) { }
    public CreateContactNumberResponse() { }

    public int ContactNumberId { get; set; }

    /// <summary>The provider's canonical (E.164) form of the registered number.</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    public string? NationalFormat { get; set; }
}
