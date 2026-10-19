using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
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
/// Subscribes the authenticated shopper to a plan (idempotent per user)
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, SubscriberContext, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, UserManager<ApplicationUser> userManager,
                ISubscriptionBillingService billing, CancellationToken cancellationToken) =>
            {
                var subscriber = await SubscriberContext.ResolveAsync(user, userManager, cancellationToken);
                if (subscriber is null)
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(request, subscriber, billing);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status202Accepted)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, SubscriberContext subscriber, ISubscriptionBillingService billing)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var result = await billing.SubscribeAsync(subscriber.Subscriber, request.PlanHandle ?? string.Empty, subscriber.RequestAborted);

        response.Subscription = result.Subscription is null ? null : SubscriptionDto.From(result.Subscription);
        switch (result.Outcome)
        {
            case SubscribeOutcome.Created:
                response.Status = CreateSubscriptionResponse.StatusCreated;
                return Results.Created("api/my-subscriptions", response);

            case SubscribeOutcome.AlreadySubscribed:
                response.Status = CreateSubscriptionResponse.StatusAlreadySubscribed;
                return Results.Ok(response);

            default:
                response.Status = CreateSubscriptionResponse.StatusPendingConfirmation;
                response.Message = "The subscription is being confirmed with the billing provider. Check api/my-subscriptions shortly.";
                return Results.Accepted("api/my-subscriptions", response);
        }
    }
}
