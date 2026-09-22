using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>Body of POST /api/contact-numbers.</summary>
public class RegisterContactNumberRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
}

/// <summary>Response of POST /api/contact-numbers — carries the new id as a top-level field.</summary>
public class RegisterContactNumberResponse
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public string? CountryCode { get; set; }
    public DateTimeOffset RegisteredAtUtc { get; set; }
}

public class ContactNumberItem
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public string? CountryCode { get; set; }
    public DateTimeOffset RegisteredAtUtc { get; set; }
}

public class ListContactNumbersResponse
{
    public List<ContactNumberItem> ContactNumbers { get; set; } = new();
}
