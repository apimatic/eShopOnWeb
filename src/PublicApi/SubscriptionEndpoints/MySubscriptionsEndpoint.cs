using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsEndpoint : IEndpoint<IResult, MySubscriptionsRequest>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (ClaimsPrincipal user, IConfiguration config, ILogger<MySubscriptionsEndpoint> logger) =>
            {
                return await HandleAsync(new MySubscriptionsRequest(), user, config, logger);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization()
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithName("GetMySubscriptions");
    }

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request)
    {
        throw new NotImplementedException("Use the route handler instead");
    }

    private static async Task<IResult> HandleAsync(MySubscriptionsRequest request, ClaimsPrincipal user, IConfiguration config, ILogger logger)
    {
        var response = new MySubscriptionsResponse(request.CorrelationId());

        try
        {
            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                logger.LogWarning("No user ID in claims");
                return Results.Unauthorized();
            }

            var maxioConfig = config.GetSection(MaxioConfiguration.CONFIG_NAME).Get<MaxioConfiguration>();
            if (maxioConfig == null)
            {
                logger.LogError("Maxio configuration not found");
                return Results.BadRequest(new { error = "Maxio configuration not found" });
            }

            var maxioService = new MaxioService(maxioConfig, logger);
            var subscriptions = await maxioService.GetUserSubscriptionsAsync(userId);

            response.Subscriptions = new List<SubscriptionDetailsDto>();
            foreach (var sub in subscriptions)
            {
                response.Subscriptions.Add(new SubscriptionDetailsDto
                {
                    Id = sub.Id,
                    PlanHandle = sub.PlanHandle,
                    PlanName = sub.PlanName,
                    Price = sub.Price,
                    Status = sub.Status,
                    NextBillingDate = sub.NextBillingDate
                });
            }

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching user subscriptions");
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}
