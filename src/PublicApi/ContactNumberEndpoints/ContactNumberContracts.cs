using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>Body of POST /api/contact-numbers.</summary>
public class RegisterContactNumberRequest
{
    /// <summary>The mobile number to register, in any form the provider can canonicalise.</summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>The caller's identity, taken from the token — never bound from the request body.</summary>
    [JsonIgnore]
    public string? CallerId { get; set; }
}

/// <summary>Response of POST /api/contact-numbers. Carries the new id as a top-level field.</summary>
public class RegisterContactNumberResponse
{
    public int ContactNumberId { get; set; }
}

/// <summary>Route binding for DELETE /api/contact-numbers/{contactNumberId}.</summary>
public class DeleteContactNumberRequest
{
    public int ContactNumberId { get; set; }

    [JsonIgnore]
    public string? CallerId { get; set; }
}

public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string E164Number { get; set; } = string.Empty;
}

public class ListContactNumbersResponse
{
    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}
