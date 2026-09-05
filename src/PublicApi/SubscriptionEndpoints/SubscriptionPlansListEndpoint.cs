using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlansListEndpoint : IEndpoint<IResult, SubscriptionPlansListRequest>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IConfiguration config, ILogger<SubscriptionPlansListEndpoint> logger) =>
            {
                var request = new SubscriptionPlansListRequest();
                return await HandleAsync(request, config, logger);
            })
            .Produces<SubscriptionPlansListResponse>()
            .WithTags("SubscriptionEndpoints")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .WithName("GetSubscriptionPlans");
    }

    public async Task<IResult> HandleAsync(SubscriptionPlansListRequest request)
    {
        throw new NotImplementedException("Use the route handler instead");
    }

    private static async Task<IResult> HandleAsync(SubscriptionPlansListRequest request, IConfiguration config, ILogger logger)
    {
        var response = new SubscriptionPlansListResponse(request.CorrelationId());

        try
        {
            var maxioConfig = config.GetSection(MaxioConfiguration.CONFIG_NAME).Get<MaxioConfiguration>();
            if (maxioConfig == null)
            {
                logger.LogError("Maxio configuration not found");
                return Results.BadRequest(new { error = "Maxio configuration not found" });
            }

            var maxioService = new MaxioService(maxioConfig, logger);
            var plans = await maxioService.GetSubscriptionPlansAsync();

            response.Plans = plans;

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching subscription plans");
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}
