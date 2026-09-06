using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List available subscription plans
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
            .Produces<SubscriptionPlanListResponse>()
            .WithName("ListSubscriptionPlans")
            .WithTags("Subscriptions");
    }

    public async Task<IResult> HandleAsync(IMaxioService maxioService)
    {
        var response = new SubscriptionPlanListResponse();

        try
        {
            var products = await maxioService.GetProductsAsync();
            response.Plans.AddRange(products.Select(p => new SubscriptionPlanDto
            {
                Id = p.Id,
                Handle = p.Handle,
                Name = p.Name,
                Description = p.Description,
                Price = p.PriceInCents / 100m,
                BillingInterval = p.IntervalUnit
            }));

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}

public class SubscriptionPlanListResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
