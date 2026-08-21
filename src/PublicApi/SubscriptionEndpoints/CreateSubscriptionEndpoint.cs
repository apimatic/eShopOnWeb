using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (CreateSubscriptionRequest request,
                    HttpContext httpContext,
                    BillingUserResolver userResolver,
                    ISubscriptionBillingService service,
                    CancellationToken cancellationToken) =>
                {
                    var user = await userResolver.ResolveAsync(httpContext.User, cancellationToken);
                    var outcome = await service.SubscribeAsync(user, request.ProductHandle, cancellationToken);
                    if (outcome.InProgress)
                    {
                        return Results.Accepted("/api/my-subscriptions", new { status = "processing" });
                    }

                    var response = SubscriptionDto.From(outcome.Subscription!);
                    return outcome.Created
                        ? Results.Created("/api/my-subscriptions", response)
                        : Results.Ok(response);
                })
            .Produces<SubscriptionDto>(StatusCodes.Status201Created)
            .Produces<SubscriptionDto>()
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithTags("SubscriptionEndpoints");
    }
}

