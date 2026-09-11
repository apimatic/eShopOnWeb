using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MinimalApi.Endpoint;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize(AuthenticationSchemes = "Bearer")]
public class SubscriptionPlanListEndpoint : IEndpoint<IResult>
{
    private readonly SubscriptionService _service;
    private readonly ILogger<SubscriptionPlanListEndpoint> _logger;

    public SubscriptionPlanListEndpoint(SubscriptionService service, ILogger<SubscriptionPlanListEndpoint> logger)
    {
        _service = service;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", async () =>
        {
            return await HandleAsync();
        })
        .Produces<List<SubscriptionPlanDto>>()
        .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        try
        {
            var client = _service.Client;
            var plans = new List<SubscriptionPlanDto>();

            // Pro plan
            try
            {
                var pro = await client.Products.ReadProductByHandle("eshop-pro", CancellationToken.None);
                if (pro?.Product != null)
                {
                    plans.Add(new SubscriptionPlanDto
                    {
                        Id = pro.Product.Id,
                        Handle = pro.Product.Handle ?? "eshop-pro",
                        Name = pro.Product.Name ?? "Pro Plan",
                        Price = 299m
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read pro plan");
            }

            // Basic plan
            try
            {
                var basic = await client.Products.ReadProductByHandle("basic-plan", CancellationToken.None);
                if (basic?.Product != null)
                {
                    plans.Add(new SubscriptionPlanDto
                    {
                        Id = basic.Product.Id,
                        Handle = basic.Product.Handle ?? "basic-plan",
                        Name = basic.Product.Name ?? "Basic Plan",
                        Price = 29m
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read basic plan");
            }

            return Results.Ok(plans);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing plans");
            return Results.StatusCode(500);
        }
    }
}
