using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioService maxioService) =>
            {
                var response = new ListSubscriptionPlansResponse();

                var config = maxioService.GetConfiguration();
                var products = await maxioService.ListProductsAsync(config.ProductFamilyHandle);

                if (products?.Products == null || !products.Products.Any())
                {
                    return Results.Ok(response);
                }

                response.Plans.AddRange(products.Products.Select(p => new SubscriptionPlanDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    Handle = p.Handle,
                    Price = p.PriceInCents / 100m,
                    Interval = p.Interval,
                    IntervalUnit = p.IntervalUnit,
                    Description = p.Description
                }));

                return Results.Ok(response);
            })
           .Produces<ListSubscriptionPlansResponse>()
           .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync()
    {
        throw new NotImplementedException();
    }
}

public class ListSubscriptionPlansResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
