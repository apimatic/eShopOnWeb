using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class UserMaxioCustomerMapping
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public int MaxioCustomerId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
