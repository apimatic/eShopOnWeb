using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscription;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the signed-in shopper to a plan. Idempotent: a repeat request for a
/// plan the shopper already holds returns the existing subscription.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService>
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
            (CreateSubscriptionRequest request, ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var userName = _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest();
        }

        SubscriptionEnrollment enrollment;
        try
        {
            enrollment = await subscriptionService.SubscribeAsync(userName, request.PlanHandle);
        }
        catch (UnknownSubscriptionPlanException)
        {
            return Results.NotFound(new { message = $"Subscription plan '{request.PlanHandle}' is not offered." });
        }

        response.CreatedNew = enrollment.CreatedNew;
        response.Subscription = new SubscriptionSummaryDto
        {
            SubscriptionId = enrollment.Subscription.SubscriptionId,
            State = enrollment.Subscription.State,
            PlanHandle = enrollment.Subscription.PlanHandle,
            PlanName = enrollment.Subscription.PlanName,
            Price = enrollment.Subscription.Price,
            Interval = enrollment.Subscription.Interval,
            IntervalUnit = enrollment.Subscription.IntervalUnit,
            NextBillingDate = enrollment.Subscription.NextBillingDate,
            ActivatedAt = enrollment.Subscription.ActivatedAt,
        };

        return enrollment.CreatedNew
            ? Results.Created($"api/my-subscriptions/{enrollment.Subscription.SubscriptionId}", response)
            : Results.Ok(response);
    }
}
