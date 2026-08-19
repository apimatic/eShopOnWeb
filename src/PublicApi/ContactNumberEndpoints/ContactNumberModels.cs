using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>Body for registering a mobile number for the signed-in shopper.</summary>
public class RegisterContactNumberRequest
{
    /// <summary>The mobile number to register, in any form the provider can canonicalise (E.164 recommended).</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}

/// <summary>A registered contact number.</summary>
public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string E164Number { get; set; } = string.Empty;
    public DateTimeOffset CreatedDate { get; set; }
}

/// <summary>Response for a successful registration; carries the new id as a top-level field.</summary>
public class RegisterContactNumberResponse
{
    public int ContactNumberId { get; set; }
    public string E164Number { get; set; } = string.Empty;
    public DateTimeOffset CreatedDate { get; set; }
}

/// <summary>Response listing the caller's registered numbers.</summary>
public class ListContactNumbersResponse
{
    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}
