using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetSubscriptionPlansEndpoint : IEndpoint<IResult, EmptyRequest, IMaxioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioService maxioService) =>
            {
                return await HandleAsync(new EmptyRequest(), maxioService);
            })
            .Produces<GetSubscriptionPlansResponse>(StatusCodes.Status200OK)
            .WithName("GetSubscriptionPlans")
            .WithTags("Subscriptions");
    }

    public async Task<IResult> HandleAsync(EmptyRequest request, IMaxioService maxioService)
    {
        try
        {
            var plans = await maxioService.GetPlansAsync();
            var response = new GetSubscriptionPlansResponse
            {
                Plans = plans.Select(SubscriptionPlanDto.FromPlan).ToList()
            };
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class EmptyRequest
{
}

public class GetSubscriptionPlansResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
