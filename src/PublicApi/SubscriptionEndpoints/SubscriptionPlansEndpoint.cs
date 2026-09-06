using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlansEndpoint : IEndpoint<IResult, IMaxioSubscriptionService>
{
    private readonly ILogger<SubscriptionPlansEndpoint> _logger;

    public SubscriptionPlansEndpoint(ILogger<SubscriptionPlansEndpoint> logger)
    {
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioSubscriptionService service) =>
            {
                return await HandleAsync(service);
            })
           .RequireAuthorization()
           .Produces<SubscriptionPlansResponse>()
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioSubscriptionService service)
    {
        try
        {
            var plans = await service.GetSubscriptionPlansAsync();
            var response = new SubscriptionPlansResponse
            {
                Plans = plans.Select(p => new PlanDto
                {
                    Handle = p.Handle,
                    Name = p.Name,
                    PriceInCents = p.PriceInCents,
                    Interval = p.Interval,
                    IntervalUnit = p.IntervalUnit
                }).ToList()
            };
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error fetching subscription plans: {ex.Message}");
            return Results.Problem(title: "Failed to retrieve subscription plans", detail: ex.Message, statusCode: 500);
        }
    }
}

public class PlanDto
{
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
}

public class SubscriptionPlansResponse
{
    public List<PlanDto> Plans { get; set; } = new();
}
