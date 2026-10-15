using System.Security.Claims;
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

/// <summary>
/// Subscribes the authenticated user to a plan
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, HttpContext, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, HttpContext httpContext, ISubscriptionService subscriptions) =>
            {
                return await HandleAsync(request, httpContext, subscriptions);
            })
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, HttpContext httpContext, ISubscriptionService subscriptions)
    {
        var userName = httpContext.User.Identity?.Name;
        if (string.IsNullOrEmpty(userName)) return Results.Unauthorized();

        var result = await subscriptions.SubscribeAsync(userName, request.PlanHandle, request.FirstName, request.LastName, httpContext.RequestAborted);
        var response = new SubscribeResponse(request.CorrelationId())
        {
            Subscription = result.Subscription,
            AlreadySubscribed = result.AlreadySubscribed,
        };
        return result.AlreadySubscribed
            ? Results.Ok(response)
            : Results.Created("api/my-subscriptions", response);
    }
}
