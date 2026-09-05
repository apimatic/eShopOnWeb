using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>Lists the active Maxio products in this application's configured product family.</summary>
public class SubscriptionPlansEndpoint : IEndpoint<IResult, SubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", async (SubscriptionService subscriptions, CancellationToken cancellationToken) =>
            Results.Ok(await subscriptions.ListPlansAsync(cancellationToken)))
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<SubscriptionPlanDto[]>()
            .WithTags("Subscriptions");
    }

    public async Task<IResult> HandleAsync(SubscriptionService subscriptions) =>
        Results.Ok(await subscriptions.ListPlansAsync(CancellationToken.None));
}
