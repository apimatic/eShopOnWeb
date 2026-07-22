using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when usage is reported against a subscription that is not in a state that accrues charges
/// (UC2). The report is refused locally; nothing is sent to the billing provider.
/// </summary>
public class SubscriptionNotBillableException : Exception
{
    public SubscriptionNotBillableException(int subscriptionId, SubscriptionLifecycleState state)
        : base($"Subscription {subscriptionId} is {state} and cannot accrue usage. Only an active or trialing subscription can be billed for usage.")
    {
        SubscriptionId = subscriptionId;
        State = state;
    }

    public int SubscriptionId { get; }

    public SubscriptionLifecycleState State { get; }
}
