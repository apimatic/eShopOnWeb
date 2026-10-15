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
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the calling shopper to a plan. Idempotent per shopper: repeating the request returns the existing
/// subscription instead of creating a second one.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, UserManager<ApplicationUser> userManager,
                ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, user, userManager, subscriptionService, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal user,
        UserManager<ApplicationUser> userManager, ISubscriptionService subscriptionService, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.ValidationProblem(new System.Collections.Generic.Dictionary<string, string[]>
            {
                [nameof(request.PlanHandle)] = new[] { "A plan handle is required. See GET api/subscription-plans." }
            });
        }

        var subscriber = await SubscriberResolver.ResolveAsync(user, userManager, request.FirstName, request.LastName);
        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        var result = await subscriptionService.SubscribeAsync(subscriber, request.PlanHandle, cancellationToken);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            PlanHandle = result.PlanHandle,
            SubscriptionReference = result.SubscriptionReference,
            Subscription = result.Subscription is null ? null : SubscriptionDto.From(result.Subscription)
        };

        switch (result.Outcome)
        {
            case SubscribeOutcome.Created:
                response.Status = "created";
                response.Message = "Subscription created.";
                return Results.Created("api/my-subscriptions", response);
            case SubscribeOutcome.AlreadySubscribed:
                response.Status = "already_subscribed";
                response.Message = "You are already subscribed to this plan.";
                return Results.Ok(response);
            default:
                response.Status = "pending";
                response.Message = "Your subscription request was sent but not yet confirmed. Check GET api/my-subscriptions shortly.";
                return Results.Accepted("api/my-subscriptions", response);
        }
    }
}
