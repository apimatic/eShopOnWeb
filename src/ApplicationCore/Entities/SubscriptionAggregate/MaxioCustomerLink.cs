using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Local claim for creating the billing-system customer of an eShop user. The primary key (user name)
/// makes a second concurrent claim fail. The billing system stays the record; this row is a claim/cache.
/// </summary>
public class MaxioCustomerLink
{
    public string UserName { get; set; } = string.Empty;
    public long? MaxioCustomerId { get; set; }
    public string Status { get; set; } = ClaimStatus.Claiming;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Local claim for creating one subscription (user + plan handle).</summary>
public class SubscriptionClaim
{
    public string UserName { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public long? MaxioSubscriptionId { get; set; }
    public string Status { get; set; } = ClaimStatus.Claiming;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class ClaimStatus
{
    public const string Claiming = "claiming";
    public const string Completed = "completed";
    /// <summary>The write may or may not have landed; settled by re-reading the billing system.</summary>
    public const string Unknown = "unknown";
}
