using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>Request to register a mobile number for the signed-in shopper.</summary>
public class RegisterContactNumberRequest
{
    /// <summary>The mobile number as the shopper typed it (any reasonable format; it is canonicalized on registration).</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}

/// <summary>Response to a successful registration. Returns the new number's identifier as a top-level field.</summary>
public class RegisterContactNumberResponse
{
    public int ContactNumberId { get; set; }

    /// <summary>The provider's canonical (E.164) form of the number, which is what gets stored.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}

/// <summary>One of the caller's registered numbers.</summary>
public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTimeOffset CreatedDate { get; set; }
}

/// <summary>The caller's registered numbers.</summary>
public class ListContactNumbersResponse
{
    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}
