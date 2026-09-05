using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public class MySubscriptionsEndpoint : IEndpoint<IResult, SubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext context, SubscriptionService subscriptions, CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(await subscriptions.GetMySubscriptionsAsync(context.User, cancellationToken));
                }
                catch (MaxioApiException)
                {
                    return Results.StatusCode(StatusCodes.Status502BadGateway);
                }
            })
            .Produces<IReadOnlyList<SubscriptionDto>>()
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("Subscriptions");
    }

    public Task<IResult> HandleAsync(SubscriptionService subscriptions) => throw new NotSupportedException();
}
