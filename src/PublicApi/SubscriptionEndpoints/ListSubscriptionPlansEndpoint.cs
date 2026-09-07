using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioApiClient maxioClient) =>
            {
                var products = await maxioClient.GetProductsAsync();

                var plans = products
                    .OrderBy(p => p.Price_in_cents)
                    .Select(p => new SubscriptionPlanDto
                    {
                        Id = p.Id,
                        Name = p.Name,
                        Handle = p.Handle,
                        Description = p.Description,
                        Price = p.Price_in_cents / 100m,
                        BillingCycle = p.Interval_unit == "month" ? "monthly" : "daily"
                    })
                    .ToList();

                return Results.Ok(new SubscriptionPlansResponse { Plans = plans });
            })
            .Produces<SubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("ListSubscriptionPlans");
    }

    public Task<IResult> HandleAsync()
    {
        throw new NotImplementedException();
    }
}
