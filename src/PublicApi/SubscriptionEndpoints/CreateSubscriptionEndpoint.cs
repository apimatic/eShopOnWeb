using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.Subscriptions;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. Ensures a Maxio customer exists for
/// the caller (idempotent) and enrolls them, returning the resulting subscription.
/// A repeated / double-clicked request returns the existing subscription rather than
/// creating a duplicate.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, SubscribeRequest, IMaxioSubscriptionService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, IMaxioSubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, IMaxioSubscriptionService subscriptionService)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        var cancellationToken = httpContext?.RequestAborted ?? CancellationToken.None;

        var subscriber = SubscriberFactory.FromPrincipal(httpContext?.User);
        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.Problem(detail: "A planHandle is required.", title: "Missing plan", statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            // Validate the requested plan against the live catalog so an unknown handle
            // gets a clean 404 (with the valid handles) instead of an upstream 422.
            var plans = await subscriptionService.GetPlansAsync(cancellationToken);
            var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, request.PlanHandle, System.StringComparison.OrdinalIgnoreCase));
            if (plan is null)
            {
                var available = string.Join(", ", plans.Select(p => p.Handle));
                return Results.Problem(
                    detail: $"Unknown plan '{request.PlanHandle}'. Available plans: {available}.",
                    title: "Plan not found",
                    statusCode: StatusCodes.Status404NotFound);
            }

            var subscription = await subscriptionService.SubscribeAsync(subscriber, plan.Handle, cancellationToken);
            return BuildResult(subscription);
        }
        catch (MaxioIntegrationException ex)
        {
            return SubscriptionProblem.From(ex, "Unable to create subscription");
        }
    }

    private static IResult BuildResult(CustomerSubscription subscription)
    {
        var dto = CustomerSubscriptionDto.From(subscription);
        var response = new SubscribeResponse
        {
            Subscription = dto,
            AlreadyExisted = subscription.AlreadyExisted,
            Message = subscription.AlreadyExisted
                ? $"You are already subscribed to {dto.PlanName ?? dto.PlanHandle} (subscription #{dto.Id})."
                : $"Subscribed to {dto.PlanName ?? dto.PlanHandle}. Your plan is {dto.State} and next bills on {FormatDate(dto)}."
        };

        // 200 for an idempotent replay of an existing subscription, 201 for a new one.
        return subscription.AlreadyExisted
            ? Results.Ok(response)
            : Results.Created("/api/my-subscriptions", response);
    }

    private static string FormatDate(CustomerSubscriptionDto dto)
        => dto.NextBillingDate?.ToString("u") ?? "an upcoming date";
}
