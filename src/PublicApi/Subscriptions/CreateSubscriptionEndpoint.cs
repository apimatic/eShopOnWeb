using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class CreateSubscriptionEndpoint : IEndpoint<IResult, SubscribeRequest, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SubscribeRequest request, IMaxioSubscriptionService service, HttpContext httpContext) =>
                await HandleAsync(request, service, httpContext))
            .Produces<SubscriptionDto>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, IMaxioSubscriptionService service)
    {
        return await HandleAsync(request, service, new DefaultHttpContext());
    }

    private static async Task<IResult> HandleAsync(SubscribeRequest request, IMaxioSubscriptionService service, HttpContext httpContext)
    {
        var userName = httpContext.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
            return Results.Unauthorized();

        var subscription = await service.SubscribeAsync(userName, request.PlanHandle, httpContext.RequestAborted);
        return Results.Ok(subscription);
    }
}
