using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists available subscription plans
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult>
{
    private readonly IMaxioSubscriptionService _subscriptionService;

    public ListSubscriptionPlansEndpoint(IMaxioSubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async () =>
            {
                return await HandleAsync();
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("ListSubscriptionPlans");
    }

    public async Task<IResult> HandleAsync()
    {
        var response = new ListSubscriptionPlansResponse();

        var plans = await _subscriptionService.GetPlansAsync();
        response.Plans.AddRange(plans.Select(p => new SubscriptionPlanDto
        {
            Id = p.Id,
            Handle = p.Handle,
            Name = p.Name,
            Description = p.Description,
            Price = p.Price,
            PriceFormatted = p.PriceFormatted,
            IntervalUnit = p.IntervalUnit,
            IntervalUnitText = p.IntervalUnitText
        }));

        return Results.Ok(response);
    }
}

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListSubscriptionPlansResponse()
    {
    }

    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
