using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class ContactNumberDto
{
    public int ContactNumberId { get; set; }

    /// <summary>The provider's canonical E.164 form of the caller's own number.</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    public DateTimeOffset RegisteredAt { get; set; }

    public static ContactNumberDto From(ContactNumber number) => new()
    {
        ContactNumberId = number.Id,
        PhoneNumber = number.PhoneNumber,
        RegisteredAt = number.RegisteredAt
    };
}
