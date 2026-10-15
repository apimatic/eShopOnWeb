using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans (Maxio products of the configured product family) a shopper can subscribe to
/// </summary>
public class SubscriptionPlansEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscriptionService service, CancellationToken ct) =>
            {
                return await HandleAsync(service, ct);
            })
            .Produces<SubscriptionPlanList>()
            .WithTags("SubscriptionEndpoints");
    }

    private static async Task<IResult> HandleAsync(SubscriptionService service, CancellationToken ct)
    {
        return Results.Ok(await service.ListPlansAsync(ct));
    }
}
