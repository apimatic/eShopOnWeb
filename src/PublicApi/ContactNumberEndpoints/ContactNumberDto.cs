using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>The shopper's view of a registered contact number (its id and canonical form).</summary>
public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;

    public static ContactNumberDto From(ContactNumber c) => new()
    {
        ContactNumberId = c.Id,
        PhoneNumber = c.PhoneNumber
    };
}
