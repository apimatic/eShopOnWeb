using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MySubscriptionsEndpoint : IEndpoint<IResult, HttpContext, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext context, ISubscriptionService subscriptionService) =>
                await HandleAsync(context, subscriptionService))
            .Produces<MySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(HttpContext context, ISubscriptionService subscriptionService)
    {
        return SubscriptionEndpointResults.ExecuteAsync(context, async () =>
        {
            var user = context.User;
            var userName = user.Identity?.Name;
            if (user.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(userName))
            {
                return Results.Unauthorized();
            }

            var subscriptions = await subscriptionService.ListMySubscriptionsAsync(userName, context.RequestAborted);
            return Results.Ok(new MySubscriptionsResponse { Subscriptions = subscriptions });
        });
    }
}
