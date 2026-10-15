using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public static class ClaimStatus
{
    public const string Pending = "Pending";
    public const string Complete = "Complete";
    /// <summary>The provider call failed after it may have been received; settled by re-reading the provider.</summary>
    public const string Unknown = "Unknown";
}

/// <summary>
/// Maps an eShopOnWeb user to its Maxio customer. The primary key (UserId) doubles as the
/// claim that keeps two concurrent requests from creating two customers.
/// </summary>
public class MaxioCustomerLink
{
    public string UserId { get; set; } = string.Empty;
    public int? MaxioCustomerId { get; set; }
    public string Status { get; set; } = ClaimStatus.Pending;
    public DateTimeOffset ClaimedAt { get; set; }
    /// <summary>Concurrency token, rotated whenever a stale claim is taken over.</summary>
    public Guid ClaimToken { get; set; } = Guid.NewGuid();
}
