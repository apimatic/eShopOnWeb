using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List available subscription plans
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, ListSubscriptionPlansRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(new ListSubscriptionPlansRequest(), subscriptionService);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListSubscriptionPlansRequest request, ISubscriptionService subscriptionService)
    {
        var result = await subscriptionService.ListPlansAsync();
        var response = new ListSubscriptionPlansResponse(request.CorrelationId());

        if (!result.IsSuccess)
            return Results.BadRequest(new { error = result.ErrorMessage });

        response.Plans = result.Plans;
        return Results.Ok(response);
    }
}
