using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, IMaxioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioService maxioService) =>
            {
                return await HandleAsync(maxioService);
            })
            .RequireAuthorization()
            .Produces<SubscriptionPlansResponse>()
            .WithName("GetSubscriptionPlans")
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioService maxioService)
    {
        var products = await maxioService.ListProductsAsync();
        var plans = products.Select(p => new SubscriptionPlanDto
        {
            Id = p.Id,
            Handle = p.Handle,
            Name = p.Name,
            Description = p.Description
        }).ToList();

        var response = new SubscriptionPlansResponse
        {
            Plans = plans
        };

        return Results.Ok(response);
    }
}

public class SubscriptionPlansResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
}
