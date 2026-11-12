using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public enum SubscriptionLinkState
{
    /// <summary>Claimed locally; the Maxio create has not been confirmed.</summary>
    Claimed = 0,
    /// <summary>Maxio confirmed the subscription; MaxioSubscriptionId is set.</summary>
    Completed = 1,
    /// <summary>The Maxio create may or may not have landed; settled by re-reading Maxio.</summary>
    Unknown = 2
}

/// <summary>
/// Claim + mapping of (eShop user, plan handle) to a Maxio subscription. The composite primary key
/// (UserId, PlanHandle) is the store-enforced refusal of a second subscribe for the same pair.
/// </summary>
public class MaxioSubscriptionLink : IAggregateRoot
{
    public string UserId { get; private set; }
    public string PlanHandle { get; private set; }
    public SubscriptionLinkState State { get; private set; }
    public int? MaxioCustomerId { get; private set; }
    public int? MaxioSubscriptionId { get; private set; }
    public string Reference { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

#pragma warning disable CS8618 // Required by Entity Framework
    private MaxioSubscriptionLink() { }
#pragma warning restore CS8618

    public MaxioSubscriptionLink(string userId, string planHandle, string reference)
    {
        UserId = userId;
        PlanHandle = planHandle;
        Reference = reference;
        State = SubscriptionLinkState.Claimed;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public void SetCustomer(int customerId) => MaxioCustomerId = customerId;

    public void Complete(int customerId, int subscriptionId)
    {
        MaxioCustomerId = customerId;
        MaxioSubscriptionId = subscriptionId;
        State = SubscriptionLinkState.Completed;
    }

    public void MarkUnknown() => State = SubscriptionLinkState.Unknown;
}
