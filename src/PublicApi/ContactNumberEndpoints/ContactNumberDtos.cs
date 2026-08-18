using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>Body of POST /api/contact-numbers.</summary>
public class RegisterContactNumberRequest
{
    /// <summary>The mobile number to register, in any format the provider can canonicalize.</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>Set from the bearer token in the endpoint; any value sent by the caller is ignored.</summary>
    internal string BuyerId { get; set; } = string.Empty;
}

public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTimeOffset RegisteredAt { get; set; }
}

/// <summary>Response of POST /api/contact-numbers — carries the new id at the top level.</summary>
public class RegisterContactNumberResponse
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
}

public class ListContactNumbersResponse
{
    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}
