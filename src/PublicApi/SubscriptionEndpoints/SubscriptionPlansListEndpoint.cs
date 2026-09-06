using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlansListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (SubscriptionService subscriptionService) =>
            {
                return await HandleAsync(subscriptionService);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("ListSubscriptionPlans");
    }

    private async Task<IResult> HandleAsync(SubscriptionService subscriptionService)
    {
        try
        {
            var response = new ListSubscriptionPlansResponse();
            var plans = await subscriptionService.GetAvailablePlansAsync();

            response.Plans.AddRange(plans.Select(p => new SubscriptionPlanResponse
            {
                Id = p.Id,
                Handle = p.Handle,
                Name = p.Name,
                PriceInCents = p.PriceInCents,
                PriceFormatted = FormatPrice(p.PriceInCents),
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit
            }));

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static string FormatPrice(long priceInCents)
    {
        return (priceInCents / 100m).ToString("C");
    }
}

public class ListSubscriptionPlansResponse
{
    public List<SubscriptionPlanResponse> Plans { get; set; } = new();
}

public class SubscriptionPlanResponse
{
    public int Id { get; set; }
    public string Handle { get; set; } = "";
    public string Name { get; set; } = "";
    public long PriceInCents { get; set; }
    public string PriceFormatted { get; set; } = "";
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "";
}
