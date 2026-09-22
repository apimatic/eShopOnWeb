using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Maps a <see cref="SubscriptionBillingException"/> to a caller-safe HTTP problem response.</summary>
internal static class BillingResultMapper
{
    public static IResult ToProblem(SubscriptionBillingException ex) => ex.Kind switch
    {
        BillingErrorKind.InvalidRequest => Results.Problem(
            detail: ex.Message, statusCode: StatusCodes.Status400BadRequest, title: "Invalid subscription request"),
        BillingErrorKind.PlanNotFound => Results.Problem(
            detail: ex.Message, statusCode: StatusCodes.Status404NotFound, title: "Subscription plan not found"),
        // ProviderUnavailable / Unknown — our credentials/quota, transport, or a provider fault: not the caller's.
        _ => Results.Problem(
            detail: ex.Message, statusCode: StatusCodes.Status502BadGateway, title: "Billing provider unavailable"),
    };

    public static MySubscriptionDto ToDto(SubscriptionSummary summary) => new()
    {
        SubscriptionId = summary.SubscriptionId,
        Reference = summary.Reference,
        PlanHandle = summary.PlanHandle,
        PlanName = summary.PlanName,
        PriceInCents = summary.PriceInCents,
        Price = summary.PriceInCents is long cents ? cents / 100m : null,
        State = summary.State,
        CurrentPeriodEndsAt = summary.CurrentPeriodEndsAt,
        NextBillingAt = summary.NextBillingAt
    };
}
