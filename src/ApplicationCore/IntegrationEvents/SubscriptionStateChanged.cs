using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Announces a lifecycle transition, carrying old and new state. Published best effort and
/// in-process only (plan §2.5).
/// </summary>
public record SubscriptionStateChanged(
    string UserReference,
    int SubscriptionId,
    SubscriptionLifecycleAction Action,
    BillingSubscriptionState PreviousState,
    BillingSubscriptionState NewState) : INotification;
