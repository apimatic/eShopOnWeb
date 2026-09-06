using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan, creating their billing customer if this is their first.
/// </summary>
/// <remarks>
/// Idempotent by design: repeating the call for a plan the shopper is already on returns the existing
/// subscription with <c>200 OK</c> instead of enrolling twice, so a double-clicked button is harmless.
/// A fresh enrollment answers <c>201 Created</c>.
/// </remarks>
public class CreateSubscriptionEndpoint
    : IEndpoint<IResult, CreateSubscriptionRequest?, HttpContext, SubscriptionsApiService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest? request, HttpContext httpContext, SubscriptionsApiService subscriptions) =>
            {
                return await HandleAsync(request, httpContext, subscriptions);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest? request,
        HttpContext httpContext,
        SubscriptionsApiService subscriptions)
    {
        // Every field of the body is optional, so an absent body is a valid "subscribe me to the default".
        request ??= new CreateSubscriptionRequest();

        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var subscription = await subscriptions.SubscribeAsync(
            httpContext.User, request.PlanHandle, httpContext.RequestAborted);

        response.Subscription = subscription;

        return subscription.WasCreatedByThisRequest
            ? Results.Created("api/my-subscriptions", response)
            : Results.Ok(response);
    }
}
