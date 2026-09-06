using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetSubscriptionPlansEndpoint : IEndpoint
{
    private readonly ISubscriptionService _subscriptionService;

    public GetSubscriptionPlansEndpoint(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (ISubscriptionService service) => await HandleAsync(service))
            .Produces<SubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionService service)
    {
        var plans = await service.GetSubscriptionPlansAsync();
        var response = new SubscriptionPlansResponse
        {
            Plans = plans.Plans.Select(p => new SubscriptionPlanResponse
            {
                Handle = p.Handle,
                Name = p.Name,
                Price = p.Price,
                BillingInterval = p.BillingInterval,
                BillingIntervalUnit = p.BillingIntervalUnit
            }).ToList()
        };
        return Results.Ok(response);
    }
}

public class SubscriptionPlansResponse
{
    public List<SubscriptionPlanResponse> Plans { get; set; } = new();
}

public class SubscriptionPlanResponse
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int BillingInterval { get; set; }
    public string BillingIntervalUnit { get; set; } = string.Empty;
}
