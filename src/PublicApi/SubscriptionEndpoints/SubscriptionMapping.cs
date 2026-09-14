using System;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps ApplicationCore billing models onto the PublicApi DTOs, and derives the Maxio
/// billing customer from the authenticated caller. The caller's login name is used as the
/// customer reference so a single eShopOnWeb user always maps to one Maxio customer, even
/// across application restarts (the in-memory database re-seeds with fresh user ids).
/// </summary>
internal static class SubscriptionMapping
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Id = plan.Id,
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        FormattedPrice = plan.FormattedPrice,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        ProductFamilyHandle = plan.ProductFamilyHandle
    };

    public static CustomerSubscriptionDto ToDto(this CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        ProductFamilyHandle = subscription.ProductFamilyHandle,
        PriceInCents = subscription.PriceInCents,
        FormattedPrice = subscription.FormattedPrice,
        Interval = subscription.Interval,
        IntervalUnit = subscription.IntervalUnit,
        NextBillingAt = subscription.NextBillingAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        CreatedAt = subscription.CreatedAt,
        AlreadyExisted = subscription.AlreadyExisted
    };

    /// <summary>
    /// Builds the Maxio <see cref="BillingCustomer"/> from the authenticated caller's login
    /// name (an email in eShopOnWeb). Throws if no caller identity is present.
    /// </summary>
    public static BillingCustomer ToBillingCustomer(string? callerName)
    {
        if (string.IsNullOrWhiteSpace(callerName))
        {
            throw new BillingException("The request is not associated with an authenticated user.", statusCode: 401);
        }

        var trimmed = callerName.Trim();
        var atIndex = trimmed.IndexOf('@');
        var firstName = atIndex > 0 ? trimmed[..atIndex] : trimmed;

        return new BillingCustomer(
            reference: trimmed,
            email: trimmed,
            firstName: firstName,
            lastName: "eShopOnWeb");
    }
}
