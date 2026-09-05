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

public sealed class SubscribeEndpoint : IEndpoint<IResult>
{
    public Task<IResult> HandleAsync() => Task.FromResult(Results.NotFound());

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", async (SubscribeRequest request, ClaimsPrincipal user, IMaxioSubscriptionService subscriptions, CancellationToken cancellationToken) =>
        {
            try
            {
                var username = user.FindFirstValue(ClaimTypes.Name);
                var subscription = await subscriptions.SubscribeAsync(username ?? string.Empty, request.ProductHandle, cancellationToken);
                return Results.Ok(subscription);
            }
            catch (Exception exception)
            {
                return SubscriptionEndpointResults.From(exception);
            }
        })
        .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
        .Accepts<SubscribeRequest>("application/json")
        .Produces<SubscriptionDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status502BadGateway)
        .WithTags("Subscriptions");
    }
}
