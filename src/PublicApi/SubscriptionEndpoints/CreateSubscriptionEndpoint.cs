using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (CreateSubscriptionRequest request, ClaimsPrincipal principal, SubscriptionService service,
                    CancellationToken cancellationToken) =>
                {
                    var result = await service.SubscribeAsync(
                        principal,
                        request.ProductHandle,
                        cancellationToken);
                    var response = new CreateSubscriptionResponse
                    {
                        Created = result.Created,
                        Subscription = result.Subscription
                    };

                    return result.Created
                        ? Results.Created("/api/my-subscriptions", response)
                        : Results.Ok(response);
                })
            .Accepts<CreateSubscriptionRequest>("application/json")
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }
}
