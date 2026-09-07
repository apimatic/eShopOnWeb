using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class MaxioCustomerMapping
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public int MaxioCustomerId { get; set; }
    public string MaxioCustomerReference { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
}
