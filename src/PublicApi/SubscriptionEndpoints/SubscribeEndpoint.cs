using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the signed-in user to a plan (idempotent per user and plan). Identity comes from the token only.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriptionService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ISubscriptionService subscriptions, HttpContext httpContext) =>
            {
                return await HandleAsync(request, subscriptions, httpContext);
            })
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .Produces<SubscribeResponse>(StatusCodes.Status202Accepted)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionService subscriptions, HttpContext httpContext)
    {
        var userName = SubscriptionEndpointSupport.UserName(httpContext.User);
        if (string.IsNullOrWhiteSpace(userName)) return Results.Unauthorized();

        var result = await subscriptions.SubscribeAsync(userName, request.PlanHandle, request.CorrelationId(), httpContext.RequestAborted);

        switch (result.Outcome)
        {
            case SubscribeOutcome.Created:
                return Results.Created("api/my-subscriptions", SubscribeResponse.From(request.CorrelationId(), result.Subscription!));
            case SubscribeOutcome.Existing:
                return Results.Ok(SubscribeResponse.From(request.CorrelationId(), result.Subscription!));
            default:
                return Results.Json(new SubscribeResponse(request.CorrelationId())
                {
                    State = "Unknown",
                    PlanHandle = request.PlanHandle,
                    Message = "The billing provider did not confirm the subscription yet. Repeat the request to settle it."
                }, statusCode: StatusCodes.Status202Accepted);
        }
    }
}
