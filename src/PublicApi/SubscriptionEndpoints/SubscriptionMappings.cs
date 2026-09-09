using System;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps between the billing domain models and the API DTOs, and projects the authenticated
/// caller (from the JWT) onto the Maxio customer identity used for idempotent enrollment.
/// </summary>
internal static class SubscriptionMappings
{
    // Namespaces the Maxio customer reference so it cannot collide with other systems that may
    // share the same Maxio site. The reference is derived deterministically from the caller's
    // identity, so the same eShopOnWeb user always resolves to the same Maxio customer.
    private const string ReferencePrefix = "eshopweb-";

    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        FormattedPrice = plan.FormattedPrice,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        RequiresPaymentMethod = plan.RequiresPaymentMethod
    };

    public static SubscriptionDto ToDto(this CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PriceInCents = subscription.PriceInCents,
        FormattedPrice = subscription.FormattedPrice,
        IntervalUnit = subscription.IntervalUnit,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextBillingAt,
        CreatedAt = subscription.CreatedAt,
        CustomerId = subscription.CustomerId,
        CustomerReference = subscription.CustomerReference
    };

    /// <summary>The stable Maxio customer reference for the authenticated caller.</summary>
    public static string ResolveCustomerReference(ClaimsPrincipal user) =>
        ReferencePrefix + GetUserName(user);

    /// <summary>Projects the authenticated caller onto the fields Maxio needs to create a customer.</summary>
    public static BillingCustomer ToBillingCustomer(ClaimsPrincipal user)
    {
        var userName = GetUserName(user);
        var reference = ReferencePrefix + userName;

        var email = userName.Contains('@', StringComparison.Ordinal)
            ? userName
            : $"{userName}@users.eshoponweb.local";

        var firstName = userName.Contains('@', StringComparison.Ordinal)
            ? userName.Split('@')[0]
            : userName;

        return new BillingCustomer(reference, email, firstName, "eShopOnWeb");
    }

    private static string GetUserName(ClaimsPrincipal user)
    {
        var userName = user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(userName))
        {
            // Should never happen behind [Authorize]; guards against a malformed token.
            throw new InvalidOperationException("The authenticated token does not carry a user identity.");
        }

        return userName.Trim();
    }
}
