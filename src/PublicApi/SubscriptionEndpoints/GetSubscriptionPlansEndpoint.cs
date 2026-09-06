using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Models.Subscription;
using Microsoft.eShopWeb.PublicApi.Services;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetSubscriptionPlansEndpoint
{
    public static void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
                async (IMaxioSubscriptionService subscriptionService) =>
                    await HandleAsync(subscriptionService))
            .WithName("GetSubscriptionPlans")
            .Produces<ListSubscriptionPlansResponse>()
            .RequireAuthorization()
            .WithTags("SubscriptionEndpoints");
    }

    private static async Task<IResult> HandleAsync(IMaxioSubscriptionService subscriptionService)
    {
        var response = new ListSubscriptionPlansResponse();

        var products = await subscriptionService.ListSubscriptionProductsAsync(CancellationToken.None);

        foreach (var product in products)
        {
            if (product.Handle != null)
            {
                var planDto = new SubscriptionPlanDto
                {
                    Handle = product.Handle,
                    Name = product.Name ?? string.Empty,
                    PriceInCents = product.PriceInCents ?? 0,
                    Interval = 1,
                    IntervalUnit = "month",
                    Description = product.Description
                };
                response.Plans.Add(planDto);
            }
        }

        return Results.Ok(response);
    }
}
