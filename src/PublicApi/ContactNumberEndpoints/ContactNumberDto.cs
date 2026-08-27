using System;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }

    public static ContactNumberDto FromEntity(ContactNumber entity) => new()
    {
        ContactNumberId = entity.Id,
        PhoneNumber = entity.PhoneNumber,
        CreatedUtc = entity.CreatedUtc
    };
}
