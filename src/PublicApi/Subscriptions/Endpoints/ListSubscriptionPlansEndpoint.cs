using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions.Endpoints;

/// <summary>
/// GET /api/subscription-plans — lists the subscription plans a shopper can subscribe to.
/// JWT-authenticated; any logged-in shopper may browse.
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ISubscriptionService subscriptions, CancellationToken ct) =>
            {
                var plans = await subscriptions.GetPlansAsync(ct);
                var response = new ListSubscriptionPlansResponse { Plans = plans.ToList() };
                return Results.Ok(response);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync() => Task.FromResult(Results.Ok());
}
