using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class ListSubscriptionPlansEndpoint
{
    public static void MapListSubscriptionPlans(this WebApplication app)
    {
        app.MapGet("api/subscription-plans", ListPlans)
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("ListSubscriptionPlans");
    }

    private static async Task<IResult> ListPlans(
        IMaxioSubscriptionService subscriptionService,
        HttpContext httpContext)
    {
        try
        {
            var plans = await subscriptionService.ListPlansAsync(httpContext.RequestAborted);
            var response = new ListSubscriptionPlansResponse
            {
                Plans = plans.ConvertAll(p => new SubscriptionPlanResponse
                {
                    Id = p.Id,
                    Name = p.Name,
                    Handle = p.Handle,
                    PriceInCents = p.PriceInCents,
                    Interval = p.Interval,
                    IntervalUnit = p.IntervalUnit
                })
            };
            return Results.Ok(response);
        }
        catch (Exception)
        {
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}

public class ListSubscriptionPlansResponse
{
    public List<SubscriptionPlanResponse> Plans { get; set; } = new();
}

public class SubscriptionPlanResponse
{
    public int? Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public long? PriceInCents { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
}
