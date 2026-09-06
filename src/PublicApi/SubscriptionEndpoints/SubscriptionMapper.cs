using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Projections between the subscription-billing domain and this API's DTOs, plus the one place that turns
/// a bearer token into the subscriber it represents.
/// </summary>
public static class SubscriptionMapper
{
    /// <summary>
    /// Claim types that may carry the caller's user name, in the order they are trusted. The tokens this
    /// API issues carry <see cref="ClaimTypes.Name"/>; the rest are accepted so a differently configured
    /// issuer still resolves to the same subscriber.
    /// </summary>
    private static readonly string[] UserNameClaimTypes =
    {
        ClaimTypes.Name,
        "unique_name",
        ClaimTypes.Email,
        "email",
        ClaimTypes.NameIdentifier,
        "sub"
    };

    /// <summary>
    /// Builds the subscriber from the caller's token. The identity always comes from the token - never
    /// from the request body - so one caller can never enroll another.
    /// </summary>
    public static SubscriberIdentity ToSubscriber(ClaimsPrincipal principal, string? firstName = null, string? lastName = null)
    {
        var userName = ResolveUserName(principal);

        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new SubscriptionBillingException(
                SubscriptionBillingFailure.InvalidRequest,
                "The access token does not identify a user.");
        }

        return new SubscriberIdentity(userName!, email: userName, firstName: firstName, lastName: lastName);
    }

    private static string? ResolveUserName(ClaimsPrincipal principal)
    {
        foreach (var claimType in UserNameClaimTypes)
        {
            var value = principal.FindFirstValue(claimType);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return principal.Identity?.Name;
    }

    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Id = plan.Id,
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Price = plan.Price,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        PaymentMethodRequired = plan.PaymentMethodRequired
    };

    public static SubscriptionDto ToDto(this CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        Reference = subscription.Reference,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PriceInCents = subscription.PriceInCents,
        Price = subscription.Price,
        Currency = subscription.Currency,
        State = subscription.State,
        IsActive = subscription.IsActive,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextBillingAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        CreatedAt = subscription.CreatedAt,
        CustomerId = subscription.CustomerId,
        CustomerReference = subscription.CustomerReference
    };

    public static List<SubscriptionPlanDto> ToDtos(this IReadOnlyList<SubscriptionPlan> plans) =>
        plans.Select(ToDto).ToList();

    public static List<SubscriptionDto> ToDtos(this IReadOnlyList<CustomerSubscription> subscriptions) =>
        subscriptions.Select(ToDto).ToList();
}
