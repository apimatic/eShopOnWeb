using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// The durable link between an eShopOnWeb user and the subscription created in Maxio.
/// Maxio remains the source of truth for the subscription's current state.
/// </summary>
public class SubscriptionMapping : BaseEntity, IAggregateRoot
{
    public string UserReference { get; private set; } = string.Empty;
    public long MaxioCustomerId { get; private set; }
    public long MaxioSubscriptionId { get; private set; }
    public string SubscriptionReference { get; private set; } = string.Empty;
    public string PlanHandle { get; private set; } = string.Empty;
    public string State { get; private set; } = string.Empty;
    public DateTimeOffset? NextBillingDate { get; private set; }

    private SubscriptionMapping()
    {
    }

    public SubscriptionMapping(
        string userReference,
        long maxioCustomerId,
        long maxioSubscriptionId,
        string subscriptionReference,
        string planHandle,
        string state,
        DateTimeOffset? nextBillingDate)
    {
        UserReference = userReference;
        MaxioCustomerId = maxioCustomerId;
        MaxioSubscriptionId = maxioSubscriptionId;
        SubscriptionReference = subscriptionReference;
        PlanHandle = planHandle;
        State = state;
        NextBillingDate = nextBillingDate;
    }

    public void UpdateFromMaxio(
        long maxioCustomerId,
        long maxioSubscriptionId,
        string subscriptionReference,
        string planHandle,
        string state,
        DateTimeOffset? nextBillingDate)
    {
        MaxioCustomerId = maxioCustomerId;
        MaxioSubscriptionId = maxioSubscriptionId;
        SubscriptionReference = subscriptionReference;
        PlanHandle = planHandle;
        State = state;
        NextBillingDate = nextBillingDate;
    }
}
