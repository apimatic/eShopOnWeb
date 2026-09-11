using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, MaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (MaxioSubscriptionService maxioService, HttpContext httpContext) =>
            {
                try
                {
                    var ct = httpContext.RequestAborted;
                    var plans = await maxioService.ListPlansAsync(ct);
                    var response = new ListSubscriptionPlansResponse();
                    response.Plans.AddRange(plans);
                    return Results.Ok(response);
                }
                catch (Exception ex)
                {
                    return Results.Problem($"Failed to list plans: {ex.Message}", statusCode: 500);
                }
            })
            .RequireAuthorization()
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(MaxioSubscriptionService service)
    {
        throw new NotImplementedException("Use AddRoute for endpoint logic.");
    }
}

public class CreateSubscriptionEndpoint : IEndpoint<IResult, MaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, MaxioSubscriptionService maxioService, HttpContext httpContext) =>
            {
                try
                {
                    var ct = httpContext.RequestAborted;
                    var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? httpContext.User.FindFirstValue("sub");
                    var email = httpContext.User.FindFirstValue(ClaimTypes.Email)
                        ?? httpContext.User.FindFirstValue("email")
                        ?? $"{userId}@placeholder.local";
                    var firstName = httpContext.User.FindFirstValue(ClaimTypes.GivenName)
                        ?? httpContext.User.FindFirstValue("given_name")
                        ?? "Unknown";
                    var lastName = httpContext.User.FindFirstValue(ClaimTypes.Surname)
                        ?? httpContext.User.FindFirstValue("family_name")
                        ?? "User";

                    if (string.IsNullOrEmpty(userId))
                        return Results.BadRequest("User ID not found in token.");

                    var subscription = await maxioService.SubscribeAsync(
                        userId, email, firstName, lastName, request.ProductHandle, ct);

                    var response = new CreateSubscriptionResponse();
                    response.Subscription = subscription;
                    return Results.Ok(response);
                }
                catch (Exception ex)
                {
                    return Results.Problem($"Failed to create subscription: {ex.Message}", statusCode: 500);
                }
            })
            .RequireAuthorization()
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(MaxioSubscriptionService service)
    {
        throw new NotImplementedException("Use AddRoute for endpoint logic.");
    }
}

public class ListMySubscriptionsEndpoint : IEndpoint<IResult, MaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (MaxioSubscriptionService maxioService, HttpContext httpContext) =>
            {
                try
                {
                    var ct = httpContext.RequestAborted;
                    var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? httpContext.User.FindFirstValue("sub");
                    var email = httpContext.User.FindFirstValue(ClaimTypes.Email)
                        ?? httpContext.User.FindFirstValue("email")
                        ?? $"{userId}@placeholder.local";
                    var firstName = httpContext.User.FindFirstValue(ClaimTypes.GivenName)
                        ?? httpContext.User.FindFirstValue("given_name")
                        ?? "Unknown";
                    var lastName = httpContext.User.FindFirstValue(ClaimTypes.Surname)
                        ?? httpContext.User.FindFirstValue("family_name")
                        ?? "User";

                    if (string.IsNullOrEmpty(userId))
                        return Results.BadRequest("User ID not found in token.");

                    var subscriptions = await maxioService.ListMySubscriptionsAsync(
                        userId, email, firstName, lastName, ct);

                    var response = new ListMySubscriptionsResponse();
                    response.Subscriptions.AddRange(subscriptions);
                    return Results.Ok(response);
                }
                catch (Exception ex)
                {
                    return Results.Problem($"Failed to list subscriptions: {ex.Message}", statusCode: 500);
                }
            })
            .RequireAuthorization()
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(MaxioSubscriptionService service)
    {
        throw new NotImplementedException("Use AddRoute for endpoint logic.");
    }
}
