using System;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>A shopper's registered contact number, as returned to that shopper (their own data).</summary>
public class ContactNumberDto
{
    public int ContactNumberId { get; set; }

    /// <summary>Provider's canonical E.164 form.</summary>
    public string Number { get; set; } = string.Empty;

    public DateTimeOffset RegisteredAt { get; set; }
}
