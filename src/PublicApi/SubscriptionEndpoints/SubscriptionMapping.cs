using System;
using System.Globalization;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Projects the subscription domain onto the API contract, and the caller's token onto the
/// subscriber it identifies.
/// </summary>
internal static class SubscriptionMapping
{
    /// <summary>
    /// Builds the subscriber from the bearer token alone. Nothing about the caller's identity is
    /// taken from the request, so a caller can only ever act on their own subscriptions.
    /// </summary>
    public static SubscriberIdentity? ToSubscriberIdentity(this ClaimsPrincipal? principal)
    {
        var userName = principal?.Identity?.Name
            ?? principal?.FindFirstValue(ClaimTypes.Name);

        return string.IsNullOrWhiteSpace(userName)
            ? null
            : new SubscriberIdentity(userName);
    }

    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Price = plan.FormattedPrice,
        BillingPeriod = DescribePeriod(plan.Interval, plan.IntervalUnit) ?? string.Empty,
        RequiresPaymentMethod = plan.RequiresPaymentMethod,
        SetupFeeInCents = plan.SetupFeeInCents,
        TrialPeriod = plan.TrialInterval > 0 ? DescribePeriod(plan.TrialInterval.Value, plan.TrialIntervalUnit) : null,
        PricePointName = plan.PricePointName
    };

    public static SubscriptionDto ToDto(this CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        IsLive = subscription.IsLive,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PriceInCents = subscription.PriceInCents,
        Price = subscription.FormattedPrice,
        Currency = subscription.Currency,
        BillingPeriod = DescribePeriod(subscription.Interval, subscription.IntervalUnit),
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextBillingAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        CreatedAt = subscription.CreatedAt,
        BalanceInCents = subscription.BalanceInCents,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod,
        Customer = subscription.Customer?.ToDto()
    };

    public static BillingCustomerDto ToDto(this BillingCustomer customer) => new()
    {
        Id = customer.Id,
        Reference = customer.Reference,
        Email = customer.Email,
        FirstName = customer.FirstName,
        LastName = customer.LastName
    };

    /// <summary>Renders "1 / month" as "month" and "3 / month" as "3 months".</summary>
    private static string? DescribePeriod(int interval, string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit) || interval <= 0)
        {
            return null;
        }

        return interval == 1
            ? unit
            : string.Create(CultureInfo.InvariantCulture, $"{interval} {unit}s");
    }
}
