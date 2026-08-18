using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>Request to register a mobile number for the signed-in shopper.</summary>
public class RegisterContactNumberRequest : BaseRequest
{
    /// <summary>The mobile number as typed by the caller; the provider's canonical form is stored.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}

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

/// <summary>Response to registration. The created identifier is a top-level field.</summary>
public class RegisterContactNumberResponse : BaseResponse
{
    public RegisterContactNumberResponse(Guid correlationId) : base(correlationId) { }
    public RegisterContactNumberResponse() { }

    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
}

public class ListContactNumbersResponse : BaseResponse
{
    public ListContactNumbersResponse(Guid correlationId) : base(correlationId) { }
    public ListContactNumbersResponse() { }

    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}
