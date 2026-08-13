using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>Register a mobile number for the signed-in shopper.</summary>
public class RegisterContactNumberRequest : BaseRequest
{
    /// <summary>The number as typed. It is validated with the provider and stored in canonical E.164 form.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}

public class ContactNumberDto
{
    public int ContactNumberId { get; set; }

    /// <summary>The provider-canonical E.164 number on file.</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public static ContactNumberDto From(ContactNumber c) => new()
    {
        ContactNumberId = c.Id,
        PhoneNumber = c.E164Number,
        CreatedAt = c.CreatedAt
    };
}

public class CreateContactNumberResponse : BaseResponse
{
    public CreateContactNumberResponse(Guid correlationId) : base(correlationId) { }
    public CreateContactNumberResponse() { }

    /// <summary>Identifier of the number that was registered.</summary>
    public int ContactNumberId { get; set; }

    public ContactNumberDto ContactNumber { get; set; } = new();
}

public class ListContactNumbersResponse : BaseResponse
{
    public ListContactNumbersResponse(Guid correlationId) : base(correlationId) { }
    public ListContactNumbersResponse() { }

    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}
