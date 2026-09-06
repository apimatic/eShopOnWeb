using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlansListEndpoint : IEndpoint<IResult, ListSubscriptionPlansRequest>
{
    private readonly MaxioSubscriptionService _subscriptionService;

    public SubscriptionPlansListEndpoint(MaxioSubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async () =>
            {
                return await HandleAsync(new ListSubscriptionPlansRequest());
            })
            .RequireAuthorization("Bearer")
            .Produces<ListSubscriptionPlansResponse>()
            .WithName("ListSubscriptionPlans")
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListSubscriptionPlansRequest request)
    {
        var response = new ListSubscriptionPlansResponse(request.CorrelationId());

        var plans = await _subscriptionService.ListPlansAsync();
        response.Plans.AddRange(plans);

        return Results.Ok(response);
    }
}
