using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class MaxioBillingCustomer
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public int MaxioCustomerId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}
