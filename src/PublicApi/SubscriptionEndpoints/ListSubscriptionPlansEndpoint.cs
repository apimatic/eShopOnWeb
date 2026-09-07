using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List available subscription plans
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, ListSubscriptionPlansRequest>
{
    private readonly IMaxioService _maxioService;

    public ListSubscriptionPlansEndpoint(IMaxioService maxioService)
    {
        _maxioService = maxioService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (CancellationToken ct) =>
            {
                return await HandleAsync(new ListSubscriptionPlansRequest());
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithName("ListSubscriptionPlans")
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListSubscriptionPlansRequest request)
    {
        try
        {
            var plans = await _maxioService.ListPlansAsync(CancellationToken.None);
            var response = new ListSubscriptionPlansResponse(request.CorrelationId())
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
