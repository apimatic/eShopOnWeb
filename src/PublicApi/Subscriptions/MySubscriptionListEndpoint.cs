using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MySubscriptionListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                    HttpContext httpContext,
                    ISubscriptionBillingService billing,
                    IBillingUserAccessor users,
                    CancellationToken cancellationToken) =>
                {
                    var user = await users.GetRequiredAsync(httpContext.User, cancellationToken);
                    return Results.Ok(await billing.ListForUserAsync(user, cancellationToken));
                })
            .Produces<SubscriptionDto[]>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("Subscriptions");
    }
}
