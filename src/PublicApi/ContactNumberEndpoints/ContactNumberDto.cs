using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTimeOffset RegisteredDate { get; set; }

    public static ContactNumberDto From(ContactNumber contactNumber) => new()
    {
        ContactNumberId = contactNumber.Id,
        PhoneNumber = contactNumber.PhoneNumber,
        RegisteredDate = contactNumber.RegisteredDate
    };
}
