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

public class GetSubscriptionPlansEndpoint : IEndpoint<IResult, EmptyRequest, SubscriptionService>
{
    private HttpContext? _httpContext;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (SubscriptionService subscriptionService, HttpContext httpContext) =>
            {
                var endpoint = new GetSubscriptionPlansEndpoint { _httpContext = httpContext };
                return await endpoint.HandleAsync(new EmptyRequest(), subscriptionService);
            })
            .Produces<GetSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(EmptyRequest request, SubscriptionService subscriptionService)
    {
        var products = await subscriptionService.GetProductsAsync();
        var response = new GetSubscriptionPlansResponse
        {
            Plans = products.Select(p => new PlanDto
            {
                Id = p.Id,
                Name = p.Name ?? string.Empty,
                Handle = p.Handle ?? string.Empty,
                Description = p.Description ?? string.Empty,
                PriceInDollars = p.PriceInCents / 100m,
                BillingInterval = p.Interval,
                BillingIntervalUnit = p.IntervalUnit ?? "month"
            }).ToList()
        };
        return Results.Ok(response);
    }
}

public class EmptyRequest
{
}

public class GetSubscriptionPlansResponse
{
    public List<PlanDto> Plans { get; set; } = new();
}

public class PlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal PriceInDollars { get; set; }
    public int BillingInterval { get; set; }
    public string BillingIntervalUnit { get; set; } = "month";
}
