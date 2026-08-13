using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>A shopper's registered contact number, returned only to its owner.</summary>
public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    public static ContactNumberDto From(ContactNumber contactNumber) => new()
    {
        ContactNumberId = contactNumber.Id,
        PhoneNumber = contactNumber.PhoneNumber,
        CreatedAt = contactNumber.CreatedAt
    };
}
