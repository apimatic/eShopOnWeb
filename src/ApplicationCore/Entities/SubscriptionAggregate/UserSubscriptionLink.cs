using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public static class SubscriptionLinkStatus
{
    /// <summary>Claimed before the Maxio call; the outcome is not recorded yet.</summary>
    public const string Claimed = "Claimed";
    public const string Active = "Active";
    /// <summary>The Maxio call may have succeeded but could not be confirmed; settled by a later re-read.</summary>
    public const string Unknown = "Unknown";
}

/// <summary>
/// Maps an eShopOnWeb user to its Maxio subscription. One subscription per user: the primary key on
/// <see cref="UserId"/> refuses a second claim before Maxio is called.
/// </summary>
public class UserSubscriptionLink
{
    public string UserId { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public int MaxioCustomerId { get; set; }
    public int? MaxioSubscriptionId { get; set; }
    public string Status { get; set; } = SubscriptionLinkStatus.Claimed;
    public DateTime CreatedUtc { get; set; }
}
