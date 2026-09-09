using System.Globalization;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps billing domain models to the API DTOs, and the JWT principal to a billing identity.
/// </summary>
internal static class SubscriptionMappings
{
    /// <summary>
    /// Builds the billing identity from the caller's token. For eShopOnWeb the JWT name claim is the
    /// user's login (an email), used both as the unique customer reference and the contact email.
    /// Returns null when the token carries no name claim.
    /// </summary>
    public static BillingUserIdentity? ToBillingIdentity(ClaimsPrincipal principal)
    {
        var userName = principal.FindFirstValue(ClaimTypes.Name);
        return string.IsNullOrEmpty(userName) ? null : new BillingUserIdentity(userName, userName);
    }

    public static SubscriptionPlanDto ToDto(SubscriptionPlanInfo plan)
    {
        decimal price = plan.PriceInCents / 100m;
        return new SubscriptionPlanDto
        {
            Handle = plan.Handle,
            ProductId = plan.ProductId,
            Name = plan.Name,
            Description = plan.Description,
            PriceInCents = plan.PriceInCents,
            Price = price,
            Interval = plan.Interval,
            IntervalUnit = plan.IntervalUnit,
            FormattedPrice = FormatPrice(price, plan.Interval, plan.IntervalUnit)
        };
    }

    public static CustomerSubscriptionDto ToDto(CustomerSubscriptionInfo subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PriceInCents = subscription.PriceInCents,
        Price = subscription.PriceInCents.HasValue ? subscription.PriceInCents.Value / 100m : null,
        Currency = subscription.Currency,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextBillingAt,
        CreatedAt = subscription.CreatedAt
    };

    private static string FormatPrice(decimal price, int interval, string? intervalUnit)
    {
        string amount = price.ToString("0.00", CultureInfo.InvariantCulture);
        if (string.IsNullOrEmpty(intervalUnit))
        {
            return amount;
        }

        return interval > 1
            ? $"{amount} per {interval} {intervalUnit}s"
            : $"{amount} per {intervalUnit}";
    }
}
