using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlanListEndpoint : IEndpoint<IResult, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", async (ISubscriptionService service, CancellationToken cancellationToken) =>
            await HandleCoreAsync(service, cancellationToken))
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<SubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionService service)
        => await HandleCoreAsync(service, CancellationToken.None);

    private static async Task<IResult> HandleCoreAsync(ISubscriptionService service, CancellationToken cancellationToken)
    {
        var plans = await service.GetPlansAsync(cancellationToken);
        return Results.Ok(new SubscriptionPlansResponse { Plans = plans });
    }
}
