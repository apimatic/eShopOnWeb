using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans available to subscribe to
/// </summary>
public class SubscriptionPlansEndpoint : IEndpoint<IResult, CancellationToken, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CancellationToken ct, ISubscriptionService subscriptions) =>
            {
                return await HandleAsync(ct, subscriptions);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CancellationToken ct, ISubscriptionService subscriptions)
    {
        var result = await subscriptions.GetPlansAsync(ct);
        return Results.Ok(new ListSubscriptionPlansResponse { Plans = result.Plans.ToList(), Truncated = result.Truncated });
    }
}
