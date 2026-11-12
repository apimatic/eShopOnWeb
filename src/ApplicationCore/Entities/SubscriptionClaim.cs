using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// Maps an eShopOnWeb user to their Maxio customer/subscription. The row is inserted (primary key = user id)
/// BEFORE any billing call is made, so a second concurrent subscribe attempt is refused by the store.
/// </summary>
public class SubscriptionClaim
{
    public const string StatusClaiming = "Claiming";
    public const string StatusActive = "Active";

    private SubscriptionClaim() { }

    public SubscriptionClaim(string userId, string planHandle)
    {
        UserId = userId;
        PlanHandle = planHandle;
        Status = StatusClaiming;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string UserId { get; private set; } = string.Empty;
    public string PlanHandle { get; private set; } = string.Empty;
    public int? MaxioCustomerId { get; private set; }
    public int? MaxioSubscriptionId { get; private set; }
    public string Status { get; private set; } = StatusClaiming;
    public DateTimeOffset CreatedAt { get; private set; }

    public void Complete(int? customerId, int subscriptionId)
    {
        MaxioCustomerId = customerId;
        MaxioSubscriptionId = subscriptionId;
        Status = StatusActive;
    }
}
