using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Maps domain results/exceptions to API DTOs and HTTP results for the subscription endpoints.</summary>
internal static class SubscriptionEndpointMapping
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlanInfo p) => new()
    {
        Handle = p.Handle,
        Name = p.Name,
        Description = p.Description,
        PriceInCents = p.PriceInCents,
        Price = p.FormattedPrice,
        Interval = p.Interval,
        IntervalUnit = p.IntervalUnit,
        ProductId = p.ProductId
    };

    public static MySubscriptionDto ToDto(this CustomerSubscriptionInfo s) => new()
    {
        SubscriptionId = s.SubscriptionId,
        PlanHandle = s.PlanHandle,
        PlanName = s.PlanName,
        PriceInCents = s.PriceInCents,
        Price = s.FormattedPrice,
        State = s.State,
        NextBillingDate = s.NextBillingDate,
        Reference = s.Reference
    };

    public static SubscribeResponse ToResponse(this SubscribeResult r) => new()
    {
        Outcome = r.Outcome.ToString(),
        PlanHandle = r.PlanHandle,
        PlanName = r.PlanName,
        PriceInCents = r.PriceInCents,
        Price = r.FormattedPrice,
        State = r.State,
        NextBillingDate = r.NextBillingDate,
        SubscriptionId = r.SubscriptionId,
        CustomerId = r.CustomerId,
        Reference = r.Reference
    };

    /// <summary>
    /// Maps a provider-boundary failure to an HTTP result: our-fault statuses (auth/quota) and unknowns
    /// become 5xx; a caller-fixable provider 4xx is passed through.
    /// </summary>
    public static IResult ToResult(this SubscriptionBillingException ex)
    {
        var status = ex.ProviderStatusCode switch
        {
            401 or 403 => StatusCodes.Status502BadGateway,
            429 => StatusCodes.Status503ServiceUnavailable,
            >= 400 and < 500 => ex.ProviderStatusCode!.Value,
            _ => StatusCodes.Status502BadGateway
        };
        return Results.Json(new SubscriptionErrorResponse(ex.Message), statusCode: status);
    }
}
