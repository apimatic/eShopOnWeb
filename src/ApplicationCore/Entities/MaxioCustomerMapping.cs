using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class MaxioCustomerMapping
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public int MaxioCustomerId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
