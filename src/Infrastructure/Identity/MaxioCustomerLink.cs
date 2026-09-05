using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>Local linkage to the customer maintained by Maxio, the billing system of record.</summary>
public class MaxioCustomerLink
{
    public string UserId { get; set; } = string.Empty;
    public int MaxioCustomerId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
