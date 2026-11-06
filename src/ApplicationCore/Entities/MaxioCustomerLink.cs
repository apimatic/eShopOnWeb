using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// Links an eShopOnWeb user to their Maxio customer. The row doubles as the claim that stops two
/// concurrent requests from creating two Maxio customers; it is a cache of provider state, not the record.
/// </summary>
public class MaxioCustomerLink
{
    /// <summary>The ApplicationUser.Id; also the Maxio customer reference.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Null while the claim is pending (the Maxio customer is not confirmed yet).</summary>
    public int? MaxioCustomerId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
