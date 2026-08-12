using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>Body of a request to register a mobile number for the signed-in shopper.</summary>
public class RegisterContactNumberRequest
{
    /// <summary>The mobile number as the caller typed it. It is validated and canonicalised by the provider.</summary>
    public string Number { get; set; } = string.Empty;
}

/// <summary>Response to registering a number. Returns the new number's identifier as a top-level field.</summary>
public class RegisterContactNumberResponse
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTimeOffset RegisteredAt { get; set; }
}

public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTimeOffset RegisteredAt { get; set; }
}

public class ListContactNumbersResponse
{
    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}
