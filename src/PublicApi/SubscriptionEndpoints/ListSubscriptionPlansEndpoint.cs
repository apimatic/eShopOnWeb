using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult>
{
    private readonly ISubscriptionService _subscriptionService;

    public ListSubscriptionPlansEndpoint(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async () =>
            {
                return await HandleAsync();
            })
           .Produces<ListSubscriptionPlansResponse>()
           .WithTags("SubscriptionEndpoints")
           .WithName("GetSubscriptionPlans");
    }

    public async Task<IResult> HandleAsync()
    {
        try
        {
            var plans = await _subscriptionService.GetSubscriptionPlansAsync();

            var response = new ListSubscriptionPlansResponse
            {
                Plans = plans
            };

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class ListSubscriptionPlansResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
