using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MySubscriptionsEndpoint : IEndpoint<IResult, SubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (SubscriptionService service, CancellationToken cancellationToken) =>
                    await HandleCoreAsync(service, cancellationToken))
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(SubscriptionService service)
    {
        return HandleCoreAsync(service, CancellationToken.None);
    }

    private static async Task<IResult> HandleCoreAsync(SubscriptionService service, CancellationToken cancellationToken)
    {
        var user = await service.GetCurrentUserAsync(cancellationToken);
        var response = new MySubscriptionsResponse();
        response.Subscriptions.AddRange(await service.GetMySubscriptionsAsync(user, cancellationToken));
        return Results.Ok(response);
    }
}
