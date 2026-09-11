using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlansEndpoint : IEndpoint<IResult, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", async (ISubscriptionService svc, HttpContext ctx) =>
        {
            return await HandleAsync(svc);
        })
        .Produces<SubscriptionPlanResponse[]>()
        .RequireAuthorization()
        .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionService svc)
    {
        var result = await svc.GetPlansAsync();
        if (result is System.Collections.IEnumerable list && result.GetType().Name.Contains("String") == false)
        {
            return Results.Ok(result);
        }
        return Results.Ok(result);
    }
}
