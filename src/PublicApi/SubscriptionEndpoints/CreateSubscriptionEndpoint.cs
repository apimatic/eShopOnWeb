using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Models;
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
/// Subscribes the calling user to a plan (idempotent per user and plan)
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal, ISubscriptionBillingService>
{
    private const int MaxPlanHandleLength = 255;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionBillingService billingService,
                CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, user, billingService, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status202Accepted)
            .Produces<ErrorDetails>(StatusCodes.Status400BadRequest)
            .Produces<ErrorDetails>(StatusCodes.Status409Conflict)
            .Produces<ErrorDetails>(StatusCodes.Status422UnprocessableEntity)
            .Produces<ErrorDetails>(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionBillingService billingService) =>
        HandleAsync(request, user, billingService, CancellationToken.None);

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal user,
        ISubscriptionBillingService billingService, CancellationToken cancellationToken)
    {
        var userName = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        var planHandle = request.PlanHandle?.Trim();
        if (string.IsNullOrEmpty(planHandle) || planHandle.Length > MaxPlanHandleLength)
        {
            return Results.BadRequest(new ErrorDetails
            {
                StatusCode = StatusCodes.Status400BadRequest,
                Message = "planHandle is required; use a handle returned by GET api/subscription-plans."
            });
        }

        var result = await billingService.SubscribeAsync(userName, planHandle, cancellationToken);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Outcome = result.Outcome.ToString(),
            PlanHandle = result.PlanHandle,
            Subscription = result.Subscription is null ? null : SubscriptionDtoMapper.ToDto(result.Subscription)
        };

        switch (result.Outcome)
        {
            case SubscribeOutcome.Created:
                response.Message = "Subscription created.";
                return Results.Created("api/my-subscriptions", response);
            case SubscribeOutcome.AlreadySubscribed:
                response.Message = "You are already subscribed to this plan.";
                return Results.Ok(response);
            default:
                response.Message = "Your subscription is being confirmed with the billing provider. " +
                                   "Check GET api/my-subscriptions shortly; repeating this request is safe.";
                return Results.Accepted("api/my-subscriptions", response);
        }
    }
}
