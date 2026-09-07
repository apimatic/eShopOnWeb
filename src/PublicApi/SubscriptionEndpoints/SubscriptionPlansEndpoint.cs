using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlansEndpoint : IEndpoint<IResult>
{
    private readonly MaxioApiClient _maxioClient;
    private readonly MaxioConfiguration _config;

    public SubscriptionPlansEndpoint(MaxioApiClient maxioClient, MaxioConfiguration config)
    {
        _maxioClient = maxioClient;
        _config = config;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/subscription-plans", Handle)
            .Produces<SubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }

    public Task<IResult> HandleAsync() => throw new NotImplementedException();

    private async Task<IResult> Handle()
    {
        try
        {
            var productsResponse = await _maxioClient.GetProductsByFamilyHandleAsync(_config.ProductFamilyHandle);

            if (productsResponse?.ProductFamily == null)
            {
                return Results.BadRequest(new { error = "Failed to fetch subscription plans" });
            }

            var plans = productsResponse.ProductFamily.Products
                .Select(p => new SubscriptionPlan
                {
                    Id = p.Id,
                    Name = p.Name,
                    Handle = p.Handle,
                    Description = p.Description,
                    PriceInCents = p.PriceInCents,
                    Interval = p.Interval,
                    IntervalUnit = p.IntervalUnit
                })
                .ToList();

            return Results.Ok(new SubscriptionPlansResponse { Plans = plans });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class SubscriptionPlansResponse
{
    public List<SubscriptionPlan> Plans { get; set; } = new();
}

public class SubscriptionPlan
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
}
