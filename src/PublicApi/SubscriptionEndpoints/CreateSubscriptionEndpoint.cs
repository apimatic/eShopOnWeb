using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, HttpContext, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, HttpContext context, ISubscriptionService subscriptionService) =>
                await HandleAsync(request, context, subscriptionService))
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        HttpContext context,
        ISubscriptionService subscriptionService)
    {
        return SubscriptionEndpointResults.ExecuteAsync(context, async () =>
        {
            var user = context.User;
            var userName = user.Identity?.Name;
            if (user.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(userName))
            {
                return Results.Unauthorized();
            }

            var subscription = await subscriptionService.SubscribeAsync(
                userName,
                request.ProductHandle,
                context.RequestAborted);
            return Results.Ok(new CreateSubscriptionResponse { Subscription = subscription });
        });
    }
}
