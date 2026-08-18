using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>A shopper's registered mobile number, as returned to that shopper.</summary>
public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string E164Number { get; set; } = string.Empty;
    public DateTimeOffset RegisteredDate { get; set; }

    public static ContactNumberDto From(ContactNumber contactNumber) => new()
    {
        ContactNumberId = contactNumber.Id,
        E164Number = contactNumber.E164Number,
        RegisteredDate = contactNumber.RegisteredDate
    };
}
