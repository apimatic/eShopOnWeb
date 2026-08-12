using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class CreateContactNumberRequest
{
    /// <summary>The mobile number to register. Any format the provider can parse; it is stored canonicalized.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}

public class CreateContactNumberResponse
{
    /// <summary>Identifier of the registered number (top-level, so the flow can be driven end to end).</summary>
    public int ContactNumberId { get; set; }

    /// <summary>The provider's canonical E.164 form that was stored.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}

/// <summary>No fields — the caller's identity comes from the token.</summary>
public class ListContactNumbersRequest
{
}

public class ListContactNumbersResponse
{
    public IEnumerable<ContactNumberDto> ContactNumbers { get; set; } = new List<ContactNumberDto>();
}

public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTimeOffset RegisteredAt { get; set; }
}

public class DeleteContactNumberRequest
{
    public int ContactNumberId { get; set; }

    public DeleteContactNumberRequest(int contactNumberId) => ContactNumberId = contactNumberId;
}
