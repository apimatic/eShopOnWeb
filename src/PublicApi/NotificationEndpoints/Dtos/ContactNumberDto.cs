using System;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints.Dtos;

public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
