using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// The eShopOnWeb user enrolling with Maxio, as resolved from the caller's identity.
/// </summary>
public sealed record MaxioSubscriber(string UserId, string UserName, string Email);

/// <summary>
/// A subscribable plan, as read from the Maxio catalog.
/// </summary>
public sealed record MaxioPlanInfo(
    string Handle,
    string Name,
    string? Description,
    decimal Price,
    long PriceInCents,
    string Interval,
    bool RequestCreditCard);

/// <summary>
/// A Maxio subscription as confirmed back to the user.
/// </summary>
public sealed record MaxioSubscriptionInfo(
    int Id,
    string State,
    string PlanHandle,
    string PlanName,
    decimal Price,
    long PriceInCents,
    DateTimeOffset? CurrentPeriodStartedAt,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? ActivatedAt,
    string? Currency);
