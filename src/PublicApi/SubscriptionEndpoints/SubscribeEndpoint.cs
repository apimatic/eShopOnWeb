using System;
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

public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (SubscribeRequest request, ClaimsPrincipal user, IConfiguration config, ILogger<SubscribeEndpoint> logger) =>
            {
                return await HandleAsync(request, user, config, logger);
            })
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithName("Subscribe");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request)
    {
        throw new NotImplementedException("Use the route handler instead");
    }

    private static async Task<IResult> HandleAsync(SubscribeRequest request, ClaimsPrincipal user, IConfiguration config, ILogger logger)
    {
        var response = new SubscribeResponse(request.CorrelationId());

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
            var subscription = await maxioService.CreateSubscriptionAsync(userId, request.PlanHandle);

            response.Subscription = new SubscriptionDetailsDto
            {
                Id = subscription.Id,
                PlanHandle = subscription.PlanHandle,
                PlanName = subscription.PlanName,
                Price = subscription.Price,
                Status = subscription.Status,
                NextBillingDate = subscription.NextBillingDate
            };

            return Results.Created($"/api/subscriptions/{subscription.Id}", response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating subscription");
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}
