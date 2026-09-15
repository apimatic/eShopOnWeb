using System;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>A registered contact number as returned to its owner. Carries the canonical E.164 form.</summary>
public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTimeOffset RegisteredAt { get; set; }
}
