using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (SubscribeRequest request, HttpContext context) =>
                    await HandleAsync(request, context))
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        SubscribeRequest request,
        HttpContext context)
    {
        var subscriptions = context.RequestServices.GetRequiredService<SubscriptionService>();
        var subscription = await subscriptions.SubscribeAsync(
            context.User, request.ProductHandle, context.RequestAborted);
        return Results.Created(
            "/api/my-subscriptions",
            new SubscribeResponse { Subscription = subscription });
    }
}
