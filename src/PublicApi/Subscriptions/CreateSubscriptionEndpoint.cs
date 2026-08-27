using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                    SubscribeRequest request,
                    HttpContext httpContext,
                    ISubscriptionBillingService billing,
                    IBillingUserAccessor users,
                    CancellationToken cancellationToken) =>
                {
                    var user = await users.GetRequiredAsync(httpContext.User, cancellationToken);
                    var subscription = await billing.SubscribeAsync(user, request.ProductHandle, cancellationToken);
                    return Results.Ok(subscription);
                })
            .Accepts<SubscribeRequest>("application/json")
            .Produces<SubscriptionDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status422UnprocessableEntity)
            .WithTags("Subscriptions");
    }
}
