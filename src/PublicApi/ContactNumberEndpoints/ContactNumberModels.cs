using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>Request to register a mobile number for the signed-in shopper.</summary>
public class RegisterContactNumberRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
}

/// <summary>A registered contact number as returned to its owner. The stored value is provider-canonical E.164.</summary>
public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string Number { get; set; } = string.Empty;
    public DateTimeOffset RegisteredDate { get; set; }

    public static ContactNumberDto From(ContactNumber c) => new()
    {
        ContactNumberId = c.Id,
        Number = c.Number,
        RegisteredDate = c.RegisteredDate
    };
}

/// <summary>Response for a successful registration; carries the new id as a top-level field.</summary>
public class RegisterContactNumberResponse
{
    public int ContactNumberId { get; set; }
    public ContactNumberDto ContactNumber { get; set; } = new();
}

public class ListContactNumbersResponse
{
    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}
