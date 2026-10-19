using System;
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
/// Subscribe the authenticated shopper to a plan. Idempotent per shopper and plan: repeating the request
/// returns the existing subscription instead of creating another one.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, UserManager<ApplicationUser> userManager,
                ISubscriptionBillingService billingService, CancellationToken cancellationToken) =>
            {
                var subscriber = await SubscriberResolver.ResolveAsync(user, userManager);
                if (subscriber is null)
                {
                    return Results.Unauthorized();
                }

                request.Subscriber = subscriber;
                return await HandleAsync(request, billingService, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status202Accepted)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billingService) =>
        HandleAsync(request, billingService, CancellationToken.None);

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billingService, CancellationToken cancellationToken)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var outcome = await billingService.SubscribeAsync(request.Subscriber, request.PlanHandle, cancellationToken);

        response.SubscriptionReference = outcome.SubscriptionReference;
        response.Subscription = outcome.Subscription is null ? null : SubscriptionDto.From(outcome.Subscription);

        switch (outcome.Status)
        {
            case SubscribeStatus.Created:
                response.Status = "created";
                response.Message = "Subscription created.";
                return Results.Created("api/my-subscriptions", response);

            case SubscribeStatus.AlreadySubscribed:
                response.Status = "already_subscribed";
                response.Message = "You already have an active subscription to this plan.";
                return Results.Ok(response);

            case SubscribeStatus.Pending:
                response.Status = "pending";
                response.Message = "Your subscription is being confirmed with the billing provider. Check api/my-subscriptions shortly.";
                return Results.Accepted("api/my-subscriptions", response);

            default:
                throw new InvalidOperationException($"Unexpected subscribe status {outcome.Status}.");
        }
    }
}
