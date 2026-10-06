using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>
/// Persists the mapping between an eShopOnWeb user and the Maxio Advanced Billing
/// customer created on their behalf. The Maxio customer <c>reference</c> is the
/// eShopOnWeb user id, which is what makes customer creation idempotent.
/// </summary>
public class MaxioCustomerMapping
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public int MaxioCustomerId { get; set; }

    public string MaxioCustomerReference { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
