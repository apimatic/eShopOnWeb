using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlansEndpoint : IEndpoint<IResult, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", async (ISubscriptionService service, CancellationToken cancellationToken) =>
            await HandleRouteAsync(service, cancellationToken))
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<SubscriptionPlansResponse>()
            .WithTags("Subscriptions");
    }

    public async Task<IResult> HandleAsync(ISubscriptionService service)
    {
        var plans = await service.ListPlansAsync(CancellationToken.None);
        var response = new SubscriptionPlansResponse();
        response.Plans.AddRange(plans);
        return Results.Ok(response);
    }

    private static async Task<IResult> HandleRouteAsync(ISubscriptionService service, CancellationToken cancellationToken)
    {
        var plans = await service.ListPlansAsync(cancellationToken);
        var response = new SubscriptionPlansResponse();
        response.Plans.AddRange(plans);
        return Results.Ok(response);
    }
}
