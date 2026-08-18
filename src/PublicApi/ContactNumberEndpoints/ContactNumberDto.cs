using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTimeOffset RegisteredAt { get; set; }

    public static ContactNumberDto FromEntity(ContactNumber number) => new()
    {
        ContactNumberId = number.Id,
        PhoneNumber = number.PhoneNumber,
        RegisteredAt = number.RegisteredAt
    };
}
