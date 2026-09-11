using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanEndpoint : IEndpoint<IResult, EmptyRequest>
{
    private readonly IMaxioService _maxio;

    public SubscriptionPlanEndpoint(IMaxioService maxio)
    {
        _maxio = maxio;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async () => await HandleAsync(new EmptyRequest()))
            .Produces<List<SubscriptionPlan>>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(EmptyRequest request)
    {
        var plans = await _maxio.GetPlansAsync();
        return Results.Ok(plans);
    }
}
