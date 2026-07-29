using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List subscription plans
/// </summary>
public class ListPlansEndpoint : IEndpoint<IResult, MaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (MaxioSubscriptionService service) =>
            {
                return await HandleAsync(service);
            })
           .Produces<ListPlansResponse>()
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MaxioSubscriptionService service)
    {
        var plans = await service.GetAvailablePlansAsync();
        return Results.Ok(new ListPlansResponse { Plans = plans });
    }
}

public class ListPlansResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
