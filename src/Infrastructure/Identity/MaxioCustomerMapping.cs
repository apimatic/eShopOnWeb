using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>
/// Local association between an Identity user and the corresponding Maxio customer.
/// Maxio remains the billing system of record; this row is a durable lookup cache.
/// </summary>
public class MaxioCustomerMapping
{
    public string UserId { get; set; } = string.Empty;
    public long MaxioCustomerId { get; set; }
    public string CustomerReference { get; set; } = string.Empty;
    public DateTimeOffset LastVerifiedAt { get; set; }
}
