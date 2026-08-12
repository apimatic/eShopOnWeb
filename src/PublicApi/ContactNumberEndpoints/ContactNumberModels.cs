using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>Body for registering a mobile number. Only <see cref="PhoneNumber"/> is client-supplied.</summary>
public class RegisterContactNumberRequest
{
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>Owner, taken from the token by the endpoint. Never bound from the request body.</summary>
    public string BuyerId { get; private set; } = string.Empty;

    public void AssignBuyer(string buyerId) => BuyerId = buyerId;
}

public class RegisterContactNumberResponse
{
    /// <summary>Identifier of the number just registered (top-level, so the flow can be driven on).</summary>
    public int ContactNumberId { get; set; }

    /// <summary>The provider's canonical E.164 form that was actually stored.</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    public bool AlreadyRegistered { get; set; }
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
