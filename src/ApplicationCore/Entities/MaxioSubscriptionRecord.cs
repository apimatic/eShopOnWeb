using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// Durable correlation data for the remote Maxio subscription. Maxio remains the billing system of record.
/// </summary>
public class MaxioSubscriptionRecord : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private MaxioSubscriptionRecord() { }
    #pragma warning restore CS8618

    public MaxioSubscriptionRecord(string userId, string planHandle, string subscriptionReference) : this()
    {
        UserId = Guard.Against.NullOrEmpty(userId, nameof(userId));
        PlanHandle = Guard.Against.NullOrEmpty(planHandle, nameof(planHandle));
        SubscriptionReference = Guard.Against.NullOrEmpty(subscriptionReference, nameof(subscriptionReference));
        CreatedAtUtc = DateTime.UtcNow;
    }

    public string UserId { get; private set; }
    public string PlanHandle { get; private set; }
    public string SubscriptionReference { get; private set; }
    public int MaxioCustomerId { get; private set; }
    public int? MaxioSubscriptionId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public void AttachMaxioIds(int customerId, int subscriptionId)
    {
        MaxioCustomerId = customerId;
        MaxioSubscriptionId = subscriptionId;
    }
}
