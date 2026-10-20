using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Maps the Maxio integration's results onto the endpoint DTOs and HTTP results.</summary>
internal static class SubscriptionEndpointMapping
{
    public static IResult Failure(MaxioBillingException ex) =>
        Results.Json(new SubscriptionErrorResponse
        {
            StatusCode = ex.StatusCode,
            Message = ex.Message,
            Errors = ex.ProviderErrors.ToList(),
        }, statusCode: ex.StatusCode);

    public static SubscriptionPlanDto ToDto(SubscriptionPlan plan) => new()
    {
        Id = plan.Id,
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Price = plan.PriceInCents / 100m,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
    };

    public static SubscriptionDto ToDto(SubscriptionSnapshot s) => new()
    {
        Id = s.Id,
        Reference = s.Reference,
        State = s.State,
        ProductHandle = s.ProductHandle,
        ProductName = s.ProductName,
        PriceInCents = s.PriceInCents,
        Price = s.PriceInCents / 100m,
        Currency = s.Currency,
        Interval = s.Interval,
        IntervalUnit = s.IntervalUnit,
        NextBillingAt = s.NextBillingAt,
        CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
        ActivatedAt = s.ActivatedAt,
        CreatedAt = s.CreatedAt,
    };
}
