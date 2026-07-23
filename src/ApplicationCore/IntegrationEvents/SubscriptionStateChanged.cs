using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Announces a lifecycle transition, carrying old state to new state (UC4, step 3). Published
/// best-effort after the provider call succeeds.
/// </summary>
/// <param name="UserName">The eShopOnWeb user reference the subscription belongs to, if known.</param>
/// <param name="Action">The transition that was requested.</param>
/// <param name="PreviousState">The state the subscription was in beforehand.</param>
/// <param name="Subscription">The subscription as the provider reported it afterwards.</param>
public record SubscriptionStateChanged(
    string? UserName,
    SubscriptionLifecycleAction Action,
    SubscriptionState PreviousState,
    Subscription Subscription) : INotification
{
    /// <summary>The state the provider reported after the transition.</summary>
    public SubscriptionState NewState => Subscription.State;
}
