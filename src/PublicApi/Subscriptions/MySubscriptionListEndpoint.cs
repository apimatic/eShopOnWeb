using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MySubscriptionListEndpoint : IEndpoint<IResult, SubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SubscriptionService service, CancellationToken cancellationToken) =>
                await HandleAsync(service, cancellationToken))
            .Produces<IReadOnlyList<MySubscriptionDto>>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(SubscriptionService service) => HandleAsync(service, CancellationToken.None);

    private static async Task<IResult> HandleAsync(SubscriptionService service, CancellationToken cancellationToken)
    {
        return Results.Ok(await service.GetMySubscriptionsAsync(cancellationToken));
    }
}
