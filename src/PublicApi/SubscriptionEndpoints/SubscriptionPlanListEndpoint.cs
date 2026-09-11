using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class SubscriptionPlanListEndpoint
{
    public static void MapRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioService maxioService) =>
            {
                var response = new ListSubscriptionPlanResponse();

                var products = await maxioService.ListProductsAsync();

                response.Plans = products.Select(p => new SubscriptionPlanDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    Handle = p.Handle,
                    Description = p.Description,
                    Price = p.PriceInCents / 100m,
                    IntervalUnit = p.IntervalUnit,
                    Interval = p.Interval
                }).ToList();

                return Results.Ok(response);
            })
            .Produces<ListSubscriptionPlanResponse>()
            .WithTags("SubscriptionEndpoints");
    }
}
