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
/// Subscribe the authenticated shopper to a plan.
/// </summary>
/// <remarks>
/// The billing customer is created on first use, so the caller never has to provision one. The
/// call is safe to repeat: an existing live subscription to the same plan is returned rather than
/// a second one being created, and supplying <c>idempotencyKey</c> makes the replay exact.
/// </remarks>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, HttpContext httpContext, ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                var subscriber = httpContext.User.ToSubscriberIdentity();
                if (subscriber is null)
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(request, subscriptionService, subscriber, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces<BlazorShared.Models.ErrorDetails>(StatusCodes.Status400BadRequest)
            .Produces<BlazorShared.Models.ErrorDetails>(StatusCodes.Status404NotFound)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService) =>
        HandleAsync(request, subscriptionService, subscriber: null, CancellationToken.None);

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        ISubscriptionService subscriptionService,
        ApplicationCore.Subscriptions.SubscriberIdentity? subscriber,
        CancellationToken cancellationToken)
    {
        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request?.PlanHandle))
        {
            var error = new BlazorShared.Models.ErrorDetails
            {
                StatusCode = StatusCodes.Status400BadRequest,
                Message = "planHandle is required. Call GET /api/subscription-plans for the available handles."
            };

            // Written through ErrorDetails.ToString() so it matches the error bodies the
            // exception middleware produces.
            return Results.Content(error.ToString(), "application/json", statusCode: StatusCodes.Status400BadRequest);
        }

        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var result = await subscriptionService.SubscribeAsync(
            subscriber, request.PlanHandle, request.IdempotencyKey, cancellationToken);

        response.Subscription = result.Subscription.ToDto();
        response.AlreadySubscribed = result.AlreadySubscribed;

        // A repeated request did not create anything, so it is not a 201.
        return result.AlreadySubscribed
            ? Results.Ok(response)
            : Results.Created("api/my-subscriptions", response);
    }
}
