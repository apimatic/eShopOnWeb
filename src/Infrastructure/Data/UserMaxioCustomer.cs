using System;

namespace Microsoft.eShopWeb.Infrastructure.Data;

public class UserMaxioCustomer
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public int MaxioCustomerId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
