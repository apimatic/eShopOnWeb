using System;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions;

public class SubscriptionEnrollment : BaseEntity, IAggregateRoot
{
    public string UserId { get; private set; } = string.Empty;
    public string PlanHandle { get; private set; } = string.Empty;
    public string SubscriptionReference { get; private set; } = string.Empty;
    public int? MaxioSubscriptionId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    protected SubscriptionEnrollment()
    {
    }

    public SubscriptionEnrollment(string userId, string planHandle, string subscriptionReference, DateTimeOffset createdAt)
    {
        UserId = Require(userId, nameof(userId));
        PlanHandle = Require(planHandle, nameof(planHandle));
        SubscriptionReference = Require(subscriptionReference, nameof(subscriptionReference));
        CreatedAt = createdAt;
    }

    public void MarkCompleted(int maxioSubscriptionId)
    {
        if (maxioSubscriptionId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxioSubscriptionId));
        }

        MaxioSubscriptionId = maxioSubscriptionId;
    }

    private static string Require(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", paramName);
        }

        return value;
    }
}
