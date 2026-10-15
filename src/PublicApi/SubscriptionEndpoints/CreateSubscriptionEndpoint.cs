using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan. Safe to repeat: a second request for the same plan returns the
/// existing subscription instead of creating another one.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, Subscriber, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, UserManager<ApplicationUser> userManager,
                ISubscriptionBillingService billing, CancellationToken cancellationToken) =>
            {
                var subscriber = await SubscriberResolver.ResolveAsync(user, userManager);
                if (subscriber is null)
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(request, subscriber, billing, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status202Accepted)
            .Produces<ErrorDetails>(StatusCodes.Status400BadRequest)
            .Produces<ErrorDetails>(StatusCodes.Status409Conflict)
            .Produces<ErrorDetails>(StatusCodes.Status422UnprocessableEntity)
            .Produces<ErrorDetails>(StatusCodes.Status502BadGateway)
            .Produces<ErrorDetails>(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, Subscriber subscriber, ISubscriptionBillingService billing) =>
        HandleAsync(request, subscriber, billing, CancellationToken.None);

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, Subscriber subscriber,
        ISubscriptionBillingService billing, CancellationToken cancellationToken)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var result = await billing.SubscribeAsync(subscriber, request.PlanHandle, cancellationToken);

        response.Outcome = result.Outcome.ToString();
        response.Subscription = result.Subscription is null ? null : SubscriptionDto.From(result.Subscription);

        switch (result.Outcome)
        {
            case SubscribeOutcome.Created:
                response.Message = "Subscription created.";
                return Results.Created("api/my-subscriptions", response);
            case SubscribeOutcome.AlreadySubscribed:
                response.Message = "You are already subscribed to this plan.";
                return Results.Ok(response);
            default:
                // The create was sent but its result could not be confirmed yet; it is settled on the next request.
                response.Message = "Your subscription request was received and is being confirmed. Check api/my-subscriptions shortly.";
                return Results.Accepted("api/my-subscriptions", response);
        }
    }
}
