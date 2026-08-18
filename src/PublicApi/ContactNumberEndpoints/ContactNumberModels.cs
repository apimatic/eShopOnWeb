using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class RegisterContactNumberRequest
{
    /// <summary>The mobile number to register, in any form the provider can canonicalize.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}

public class RegisterContactNumberResponse
{
    public int ContactNumberId { get; set; }

    /// <summary>The provider's canonical (E.164) form that was stored.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
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
