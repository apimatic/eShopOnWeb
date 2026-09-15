using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>
/// Local pointer to the Maxio customer that belongs to an eShopOnWeb user.
/// Maxio remains the billing system of record.
/// </summary>
public class MaxioCustomerLink
{
    public string UserId { get; set; } = string.Empty;
    public long MaxioCustomerId { get; set; }
    public string CustomerReference { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
