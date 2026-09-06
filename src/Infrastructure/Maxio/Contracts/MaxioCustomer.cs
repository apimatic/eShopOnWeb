using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

/// <summary>Maxio customer - the billing-system counterpart of an eShopOnWeb account.</summary>
public class MaxioCustomer
{
    public long Id { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? Email { get; set; }

    public string? Organization { get; set; }

    /// <summary>Our own stable identifier for the customer. Unique per site, which is what makes it idempotent.</summary>
    public string? Reference { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}
