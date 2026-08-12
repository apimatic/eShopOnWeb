using System;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>A shopper's registered number, in the provider's canonical E.164 form.</summary>
public class ContactNumberDto
{
    public int Id { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTimeOffset RegisteredAt { get; set; }
}
