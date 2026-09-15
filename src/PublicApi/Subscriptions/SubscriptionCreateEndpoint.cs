using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionCreateEndpoint : IEndpoint<IResult, SubscribeRequest, SubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SubscribeRequest request, SubscriptionService service, CancellationToken cancellationToken) =>
                await HandleAsync(request, service, cancellationToken))
            .Produces<MySubscriptionDto>(StatusCodes.Status201Created)
            .Produces<MySubscriptionDto>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(SubscribeRequest request, SubscriptionService service) =>
        HandleAsync(request, service, CancellationToken.None);

    private static async Task<IResult> HandleAsync(SubscribeRequest request, SubscriptionService service, CancellationToken cancellationToken)
    {
        var result = await service.SubscribeAsync(request, cancellationToken);
        return result.Created
            ? Results.Created($"api/subscriptions/{result.Subscription.Id}", result.Subscription)
            : Results.Ok(result.Subscription);
    }
}
