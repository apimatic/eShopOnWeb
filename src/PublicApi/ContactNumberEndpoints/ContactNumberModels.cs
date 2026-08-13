using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public record RegisterContactNumberRequest(string? PhoneNumber);

public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTimeOffset RegisteredAt { get; set; }

    public static ContactNumberDto From(ContactNumber c) => new()
    {
        ContactNumberId = c.Id,
        PhoneNumber = c.PhoneNumber,
        RegisteredAt = c.RegisteredAt
    };
}

/// <summary>Response to registering a number. Carries <see cref="ContactNumberId"/> as a top-level field.</summary>
public class RegisterContactNumberResponse
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTimeOffset RegisteredAt { get; set; }
}

public class ListContactNumbersResponse
{
    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}
