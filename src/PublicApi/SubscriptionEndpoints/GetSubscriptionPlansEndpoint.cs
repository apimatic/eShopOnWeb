using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List available subscription plans
/// </summary>
public class GetSubscriptionPlansEndpoint : IEndpoint<IResult, GetSubscriptionPlansRequest>
{
    private readonly ISubscriptionService _subscriptionService;

    public GetSubscriptionPlansEndpoint(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async () => await HandleAsync(new GetSubscriptionPlansRequest()))
            .Produces<GetSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(GetSubscriptionPlansRequest request)
    {
        var response = new GetSubscriptionPlansResponse(request.CorrelationId());

        var plans = await _subscriptionService.GetAvailablePlansAsync();
        foreach (var plan in plans)
        {
            response.Plans.Add(new SubscriptionPlanResponse
            {
                Handle = plan.Handle,
                Name = plan.Name,
                Description = plan.Description,
                Price = plan.Price,
                PriceDisplay = plan.PriceDisplay,
                Interval = plan.Interval,
                IntervalUnit = plan.IntervalUnit
            });
        }

        return Results.Ok(response);
    }
}

public class GetSubscriptionPlansRequest : BaseRequest
{
}

public class GetSubscriptionPlansResponse : BaseResponse
{
    public GetSubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public List<SubscriptionPlanResponse> Plans { get; } = [];
}

public class SubscriptionPlanResponse
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string PriceDisplay { get; set; } = string.Empty;
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
}
