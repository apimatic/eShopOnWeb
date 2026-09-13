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

/// <summary>
/// List Subscription Plans
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult, IMaxioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioService maxioService) =>
            {
                return await HandleAsync(maxioService);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioService maxioService)
    {
        var response = new ListSubscriptionPlansResponse();
        var products = await maxioService.GetAvailablePlansAsync();

        response.Plans = products.Select(p => new SubscriptionPlanDto
        {
            Id = p.Id,
            Name = p.Name,
            Handle = p.Handle ?? string.Empty,
            Description = p.Description,
            PriceInCents = p.PriceInCents,
            Interval = p.Interval,
            IntervalUnit = p.IntervalUnit,
            RequireCreditCard = p.RequireCreditCard
        }).ToList();

        return Results.Ok(response);
    }
}
