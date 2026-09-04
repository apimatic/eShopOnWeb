using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public sealed class MaxioSubscriptionMapping : BaseEntity, IAggregateRoot
{
    private MaxioSubscriptionMapping() { }

    public MaxioSubscriptionMapping(
        string applicationUserId,
        string planHandle,
        int maxioCustomerId,
        int maxioSubscriptionId,
        string maxioReference)
    {
        ApplicationUserId = applicationUserId;
        PlanHandle = planHandle;
        MaxioCustomerId = maxioCustomerId;
        MaxioSubscriptionId = maxioSubscriptionId;
        MaxioReference = maxioReference;
        CreatedUtc = DateTimeOffset.UtcNow;
    }

    public string ApplicationUserId { get; private set; } = string.Empty;
    public string PlanHandle { get; private set; } = string.Empty;
    public int MaxioCustomerId { get; private set; }
    public int MaxioSubscriptionId { get; private set; }
    public string MaxioReference { get; private set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; private set; }

    public void ReplaceSubscription(int maxioSubscriptionId)
    {
        MaxioSubscriptionId = maxioSubscriptionId;
    }
}
