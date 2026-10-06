using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Ardalis.Result;
using IResult = Microsoft.AspNetCore.Http.IResult;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan. Idempotent: repeating the same
/// subscription returns the existing subscription without duplicating it.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ISubscriptionService subscriptionService, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, subscriptionService, user);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService)
    {
        return await HandleAsync(request, subscriptionService, new ClaimsPrincipal());
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService, ClaimsPrincipal user)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var username = user.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(username))
        {
            return Results.Unauthorized();
        }

        var applicationUser = await _userManager.FindByNameAsync(username);
        if (applicationUser is null)
        {
            return Results.Unauthorized();
        }

        var result = await subscriptionService.SubscribeAsync(
            applicationUser.Id, applicationUser.Email ?? username, request.PlanHandle);

        if (result.Status == ResultStatus.NotFound)
        {
            return Results.NotFound(new { correlationId = response.CorrelationId(), errors = result.Errors });
        }

        if (!result.IsSuccess)
        {
            // A Maxio API rejection is an upstream (gateway) failure, not a defect in this API.
            return Results.Json(new { correlationId = response.CorrelationId(), errors = result.Errors },
                statusCode: StatusCodes.Status502BadGateway);
        }

        response.Subscription = MapSubscription(result.Value);
        return Results.Ok(response);
    }

    internal static SubscriptionDto MapSubscription(Subscription subscription)
    {
        return new SubscriptionDto
        {
            Id = subscription.Id,
            PlanHandle = subscription.PlanHandle,
            PlanName = subscription.PlanName,
            State = subscription.State,
            PriceInCents = subscription.PriceInCents,
            Price = subscription.PriceInCents / 100m,
            Currency = subscription.Currency,
            BillingInterval = subscription.BillingInterval,
            BillingIntervalUnit = subscription.BillingIntervalUnit,
            NextBillingDateUtc = subscription.NextBillingDateUtc,
            MaxioSubscriptionId = subscription.MaxioSubscriptionId,
            MaxioCustomerId = subscription.MaxioCustomerId
        };
    }
}