using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Identity of the eShopOnWeb user that is subscribing. Carried from the JWT so the
/// billing layer never has to reach back into ASP.NET Identity. The <see cref="UserId"/>
/// is the stable, idempotent external reference used as the Maxio customer reference.
/// </summary>
public record SubscriberIdentity(string UserId, string Email, string? FirstName, string? LastName);

/// <summary>
/// A subscription plan the shopper can choose from. Maps to a Maxio "product" that lives
/// under the configured product family. Prices are expressed in major currency units.
/// </summary>
public record SubscriptionPlan(
    int Id,
    string Handle,
    string Name,
    string? Description,
    decimal Price,
    string Currency,
    string Interval,
    string ProductFamilyHandle);

/// <summary>
/// A subscription belonging to the current shopper, projected for confirmation / display.
/// </summary>
public record CustomerSubscription(
    int Id,
    string State,
    string PlanName,
    string? PlanHandle,
    decimal Price,
    string Currency,
    string Interval,
    DateTimeOffset? CurrentPeriodStartsAt,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? NextBillingDate,
    int CustomerId,
    string? CustomerReference);

/// <summary>
/// Command describing which plan the shopper wants and who they are.
/// </summary>
public record SubscribeCommand(SubscriberIdentity Subscriber, string PlanHandle);

/// <summary>
/// Outcome of a subscribe attempt. <see cref="AlreadyExisted"/> is true when an active
/// subscription to the plan was already present (idempotent double-click), in which case
/// the existing subscription is returned unchanged.
/// </summary>
public record SubscribeResult(CustomerSubscription Subscription, bool AlreadyExisted);
