using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class MaxioCustomerLink
{
    public string UserId { get; set; } = null!;
    public int MaxioCustomerId { get; set; }
    public string CustomerReference { get; set; } = null!;
    public DateTimeOffset UpdatedAt { get; set; }
}
