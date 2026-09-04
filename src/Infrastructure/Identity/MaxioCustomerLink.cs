using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>
/// Durable association between an eShopOnWeb identity and its Maxio customer.
/// The Maxio customer remains the billing system of record.
/// </summary>
public class MaxioCustomerLink
{
    public string UserId { get; set; } = string.Empty;
    public int MaxioCustomerId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
