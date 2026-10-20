using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public enum MaxioSubscriptionLinkState
{
    /// <summary>Claimed by a subscribe call that is still talking to Maxio.</summary>
    Claiming = 0,
    Active = 1,
    /// <summary>A write to Maxio may or may not have landed; settled by a later call.</summary>
    Unknown = 2
}

/// <summary>
/// Maps one shopper to their Maxio customer and subscription. The primary key (UserId) is the
/// duplicate-claim guard: only one subscribe call per user can insert the row.
/// </summary>
public class MaxioSubscriptionLink
{
    public string UserId { get; set; } = string.Empty;
    public int? MaxioCustomerId { get; set; }
    public int? MaxioSubscriptionId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public MaxioSubscriptionLinkState State { get; set; }
    public DateTimeOffset ClaimedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
