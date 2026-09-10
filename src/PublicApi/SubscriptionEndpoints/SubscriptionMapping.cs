using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps application-core subscription DTOs onto the PublicApi wire DTOs, and translates the single
/// <see cref="SubscriptionBillingException"/> into an appropriate HTTP problem response.
/// </summary>
internal static class SubscriptionMapping
{
    public static SubscriptionPlanDto ToDto(SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Price = plan.PriceInCents / 100m,
        IntervalCount = plan.IntervalCount,
        IntervalUnit = plan.IntervalUnit
    };

    public static SubscriptionDto ToDto(CustomerSubscriptionInfo subscription) => new()
    {
        Id = subscription.Id,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        State = subscription.State,
        PriceInCents = subscription.PriceInCents,
        Price = subscription.PriceInCents / 100m,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextBillingAt,
        ActivatedAt = subscription.ActivatedAt
    };

    public static IResult ToProblem(SubscriptionBillingException exception)
    {
        var status = exception.Kind switch
        {
            SubscriptionBillingErrorKind.InvalidRequest => StatusCodes.Status400BadRequest,
            SubscriptionBillingErrorKind.NotFound => StatusCodes.Status404NotFound,
            SubscriptionBillingErrorKind.ProviderUnavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status502BadGateway
        };
        return Results.Problem(detail: exception.Message, statusCode: status, title: "Subscription billing error");
    }
}
