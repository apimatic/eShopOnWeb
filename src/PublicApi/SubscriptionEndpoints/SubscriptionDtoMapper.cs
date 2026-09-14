using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionDtoMapper
{
    public static SubscriptionDto ToDto(SubscriptionEnrollment enrollment) => new()
    {
        SubscriptionId = enrollment.SubscriptionId,
        PlanHandle = enrollment.PlanHandle,
        PlanName = enrollment.PlanName,
        PriceInCents = enrollment.PriceInCents,
        State = enrollment.State,
        CreatedAt = enrollment.CreatedAt,
        CurrentPeriodEndsAt = enrollment.CurrentPeriodEndsAt,
        NextBillingAt = enrollment.NextBillingAt,
        AlreadyExisted = enrollment.AlreadyExisted
    };
}
