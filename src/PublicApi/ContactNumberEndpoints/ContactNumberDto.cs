using System;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class ContactNumberDto
{
    public int ContactNumberId { get; set; }

    /// <summary>The provider's canonical E.164 form of the number.</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    public DateTimeOffset RegisteredAt { get; set; }
}
