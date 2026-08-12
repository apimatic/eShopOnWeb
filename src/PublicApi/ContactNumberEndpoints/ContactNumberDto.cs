using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTimeOffset RegisteredAt { get; set; }

    public static ContactNumberDto From(ContactNumber contactNumber) => new()
    {
        ContactNumberId = contactNumber.Id,
        PhoneNumber = contactNumber.PhoneNumber,
        RegisteredAt = contactNumber.RegisteredAt
    };
}
</content>
