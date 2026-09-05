using System;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MySubscriptionsEndpoint : IEndpoint<IResult>
{
    public Task<IResult> HandleAsync() => Task.FromResult(Results.NotFound());

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", async (ClaimsPrincipal user, IMaxioSubscriptionService subscriptions, CancellationToken cancellationToken) =>
        {
            try
            {
                var username = user.FindFirstValue(ClaimTypes.Name);
                var items = await subscriptions.ListMySubscriptionsAsync(username ?? string.Empty, cancellationToken);
                return Results.Ok(new MySubscriptionsResponse { Subscriptions = items });
            }
            catch (Exception exception)
            {
                return SubscriptionEndpointResults.From(exception);
            }
        })
        .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
        .Produces<MySubscriptionsResponse>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status502BadGateway)
        .WithTags("Subscriptions");
    }
}
