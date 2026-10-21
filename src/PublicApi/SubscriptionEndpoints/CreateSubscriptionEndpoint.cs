using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the signed-in shopper to a plan (idempotent: a repeat never creates a second subscription)
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, UserManager<ApplicationUser> userManager,
                ISubscriptionService subscriptionService, HttpContext httpContext) =>
            {
                var subscriber = await SubscriptionEndpointSupport.ResolveSubscriberAsync(user, userManager);
                if (subscriber is null)
                    return SubscriptionEndpointSupport.UnknownUser();

                request.Subscriber = subscriber;
                return await HandleAsync(request, subscriptionService, httpContext);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status503ServiceUnavailable)
            .WithTags(SubscriptionEndpointSupport.Tag);
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService) =>
        HandleAsync(request, subscriptionService, null);

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService,
        HttpContext? httpContext)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());
        if (request.Subscriber is null)
            return SubscriptionEndpointSupport.UnknownUser();
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
            return Results.BadRequest(new BlazorShared.Models.ErrorDetails { StatusCode = 400, Message = "planHandle is required." });

        SubscribeResult result;
        try
        {
            // Only the reads before the first write honour the caller's token; writes are never abandoned.
            result = await subscriptionService.SubscribeAsync(request.Subscriber, request.PlanHandle,
                httpContext?.RequestAborted ?? CancellationToken.None);
        }
        catch (SubscriptionBillingException ex) when (httpContext is not null)
        {
            return SubscriptionEndpointSupport.FromBillingException(ex, httpContext);
        }

        response.SignupId = result.SignupId;
        response.Outcome = result.Outcome switch
        {
            SubscribeOutcome.Created => "created",
            SubscribeOutcome.AlreadySubscribed => "already_subscribed",
            SubscribeOutcome.ActionRequired => "action_required",
            _ => "pending",
        };
        response.Subscription = result.Subscription is null ? null : SubscriptionDto.From(result.Subscription);
        response.ActionLink = result.ActionLink;

        switch (result.Outcome)
        {
            case SubscribeOutcome.Created:
                return Results.Created("api/my-subscriptions", response);
            case SubscribeOutcome.AlreadySubscribed:
                return Results.Ok(response);
            default:
                // Not a failure: the signup is settled later under the same reference (poll my-subscriptions).
                if (httpContext is not null)
                    SubscriptionEndpointSupport.SetRetryAfter(httpContext, result.RetryAfter);
                return Results.Accepted("api/my-subscriptions", response);
        }
    }
}
