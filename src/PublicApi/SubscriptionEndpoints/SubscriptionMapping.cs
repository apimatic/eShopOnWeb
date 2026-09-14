using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionMapping
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        Price = plan.PriceInCents / 100m,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit
    };

    public static MySubscriptionDto ToDto(this MaxioSubscription subscription) => new()
    {
        SubscriptionId = subscription.Id,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        Price = subscription.PriceInCents / 100m,
        Interval = subscription.Interval,
        IntervalUnit = subscription.IntervalUnit,
        State = subscription.State,
        NextBillingDate = subscription.NextAssessmentAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt
    };

    /// <summary>
    /// eShopOnWeb's ApplicationUser (a plain IdentityUser) has no first/last name fields and
    /// uses the same value for UserName and Email, so we derive a display name from the email
    /// local-part purely so Maxio's (required) customer name fields have something sensible.
    /// </summary>
    public static MaxioCustomerProfile ToMaxioCustomerProfile(this string username)
    {
        var localPart = username.Split('@')[0];
        var firstName = string.IsNullOrWhiteSpace(localPart) ? "eShopOnWeb" : localPart;

        return new MaxioCustomerProfile
        {
            Reference = username,
            Email = username,
            FirstName = firstName,
            LastName = "Customer"
        };
    }
}
