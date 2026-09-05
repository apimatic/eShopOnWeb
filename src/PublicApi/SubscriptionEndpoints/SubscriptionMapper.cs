using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionMapper
{
    public static SubscriptionPlanDto ToDto(MaxioPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Price = plan.PriceInCents / 100m,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        RequiresPaymentMethod = plan.RequiresPaymentMethod
    };

    public static SubscriptionDto ToDto(MaxioSubscription subscription) => new()
    {
        SubscriptionId = subscription.Id,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PriceInCents = subscription.PriceInCents,
        Price = subscription.PriceInCents / 100m,
        State = subscription.State,
        NextBillingDate = subscription.NextBillingAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt
    };

    /// <summary>
    /// eShopOnWeb's ApplicationUser (an IdentityUser) carries no real name - derive a
    /// reasonable display name from the email's local part for the Maxio customer record.
    /// </summary>
    public static (string FirstName, string LastName) DeriveNameFromEmail(string email)
    {
        var localPart = email.Split('@')[0];
        return (localPart, "Customer");
    }
}
