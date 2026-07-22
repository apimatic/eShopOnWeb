using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Announces that an eShopOnWeb user now holds a live subscription. Published best effort and
/// in-process only (plan §2.5) — a failing handler never rolls the enrolment back.
/// </summary>
public record SubscriptionActivated(string UserReference, BillingSubscription Subscription) : INotification;
