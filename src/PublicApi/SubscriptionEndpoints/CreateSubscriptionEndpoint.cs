using System;
using System.Collections.Generic;
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
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated caller to a plan. The caller's identity comes from
/// the JWT; a Maxio customer is ensured idempotently for the caller and the created
/// (or already existing) subscription is confirmed back with plan, price, state and
/// next billing date.
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

        var user = _httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal();
        var buyerId = user.FindFirstValue(ClaimTypes.Name) ?? user.FindFirstValue("name") ?? user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new { error = "A planHandle is required." });
        }

        SubscribeResult result;
        try
        {
            result = await subscriptionService.SubscribeAsync(buyerId, request.PlanHandle);
        }
        catch (SubscriptionPlanNotFoundException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
        catch (MaxioApiException ex)
        {
            return Results.Problem(
                title: "The billing system rejected the request.",
                statusCode: 502,
                extensions: new Dictionary<string, object?> { ["maxioStatus"] = ex.StatusCode });
        }

        response.WasCreated = result.WasCreated;
        response.Subscription = ToDto(result.Subscription);

        return result.WasCreated
            ? Results.Created($"api/my-subscriptions", response)
            : Results.Ok(response);
    }

    internal static SubscriptionDto ToDto(SubscriptionDetails details) => new()
    {
        Id = details.MaxioSubscriptionId,
        PlanHandle = details.PlanHandle,
        PlanName = details.PlanName,
        State = details.State,
        Price = details.PriceInCents / 100m,
        PriceInCents = details.PriceInCents,
        Interval = details.Interval,
        IntervalUnit = details.IntervalUnit,
        NextBillingDate = details.NextBillingAt,
        ActivatedAt = details.ActivatedAt
    };
}
