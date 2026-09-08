using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionMappings
{
    public static SubscriptionPlanDto ToPlanDto(this MaxioProduct product, MaxioProductFamily? productFamily)
    {
        return new SubscriptionPlanDto
        {
            Id = product.Id,
            Handle = product.Handle ?? string.Empty,
            Name = product.Name,
            Description = product.Description,
            PriceInCents = product.PriceInCents,
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit,
            InitialChargeInCents = product.InitialChargeInCents,
            TrialPriceInCents = product.TrialPriceInCents,
            TrialInterval = product.TrialInterval,
            TrialIntervalUnit = product.TrialIntervalUnit,
            ExpirationIntervalUnit = product.ExpirationIntervalUnit,
            RequiresCreditCard = product.RequiresCreditCard,
            Taxable = product.Taxable,
            ProductFamilyName = productFamily?.Name,
            ProductFamilyHandle = productFamily?.Handle
        };
    }

    public static SubscriptionDto ToSubscriptionDto(this MaxioSubscription subscription)
    {
        return new SubscriptionDto
        {
            Id = subscription.Id,
            State = subscription.State,
            PlanId = subscription.Product?.Id,
            PlanHandle = subscription.Product?.Handle,
            PlanName = subscription.Product?.Name,
            PriceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents,
            Interval = subscription.Product?.Interval,
            IntervalUnit = subscription.Product?.IntervalUnit,
            BalanceInCents = subscription.BalanceInCents,
            CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextAssessmentAt = subscription.NextAssessmentAt,
            ActivatedAt = subscription.ActivatedAt,
            CreatedAt = subscription.CreatedAt,
            CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod,
            PaymentCollectionMethod = subscription.PaymentCollectionMethod,
            CustomerId = subscription.Customer?.Id ?? 0,
            CustomerReference = subscription.Customer?.Reference,
            CustomerEmail = subscription.Customer?.Email
        };
    }
}
