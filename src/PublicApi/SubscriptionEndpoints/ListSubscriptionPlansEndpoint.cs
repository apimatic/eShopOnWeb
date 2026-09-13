using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List available subscription plans
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, ListSubscriptionPlansRequest, MaxioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (MaxioService maxioService) =>
            {
                return await HandleAsync(new ListSubscriptionPlansRequest(), maxioService);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListSubscriptionPlansRequest request, MaxioService maxioService)
    {
        var response = new ListSubscriptionPlansResponse(request.CorrelationId());

        try
        {
            var plans = await maxioService.ListSubscriptionPlansAsync(default);
            response.Plans = plans;
            return Results.Ok(response);
        }
        catch (Exception)
        {
            response.ErrorMessage = "Failed to retrieve subscription plans.";
            return Results.StatusCode(502);
        }
    }
}
