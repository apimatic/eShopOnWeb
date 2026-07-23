using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Announces that an eShopOnWeb user was successfully enrolled in a plan (UC1, step 6).
/// Published best-effort after the provider call succeeds; a handler failure never rolls the
/// subscription back.
/// </summary>
/// <param name="UserName">The eShopOnWeb user reference the subscription belongs to.</param>
/// <param name="Subscription">The subscription as the provider reported it.</param>
public record SubscriptionActivated(string UserName, Subscription Subscription) : INotification;
