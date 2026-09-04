using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlansEndpoint : IEndpoint<IResult, SubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (SubscriptionService service, CancellationToken cancellationToken) =>
                    await HandleCoreAsync(service, cancellationToken))
            .Produces<SubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(SubscriptionService service)
    {
        return HandleCoreAsync(service, CancellationToken.None);
    }

    private static async Task<IResult> HandleCoreAsync(SubscriptionService service, CancellationToken cancellationToken)
    {
        var response = new SubscriptionPlansResponse();
        response.Plans.AddRange(await service.GetPlansAsync(cancellationToken));
        return Results.Ok(response);
    }
}
