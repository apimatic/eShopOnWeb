using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.MaxioEndpoints;

public class SubscriptionPlanListEndpoint : IEndpoint<IResult, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioSubscriptionService maxioService) =>
            {
                return await HandleAsync(maxioService);
            })
           .Produces<ListSubscriptionPlansResponse>()
           .WithTags("MaxioEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioSubscriptionService maxioService)
    {
        var plans = await maxioService.ListPlansAsync();
        var response = new ListSubscriptionPlansResponse
        {
            Plans = plans
        };
        return Results.Ok(response);
    }
}

public class ListSubscriptionPlansResponse
{
    public IReadOnlyList<PlanDto> Plans { get; set; } = new List<PlanDto>();
}
