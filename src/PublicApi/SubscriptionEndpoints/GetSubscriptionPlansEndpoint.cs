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

public class GetSubscriptionPlansEndpoint : IEndpoint<IResult, GetSubscriptionPlansRequest, IMaxioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioService maxioService) =>
            {
                return await HandleAsync(new GetSubscriptionPlansRequest(), maxioService);
            })
            .Produces<GetSubscriptionPlansResponse>(StatusCodes.Status200OK)
            .WithTags("Subscriptions");
    }

    public async Task<IResult> HandleAsync(GetSubscriptionPlansRequest request, IMaxioService maxioService)
    {
        var response = new GetSubscriptionPlansResponse(request.CorrelationId());

        var plans = await maxioService.GetPlansAsync();
        response.Plans = plans.Select(p => new SubscriptionPlanDto
        {
            Id = p.Id,
            Name = p.Name,
            Handle = p.Handle,
            Description = p.Description,
            Price = p.PriceInCents / 100m,
            Interval = p.Interval,
            IntervalUnit = p.IntervalUnit
        }).ToList();

        return Results.Ok(response);
    }
}

public class GetSubscriptionPlansRequest
{
    public string CorrelationId() => Guid.NewGuid().ToString();
}

public class GetSubscriptionPlansResponse
{
    public string CorrelationId { get; set; }

    public GetSubscriptionPlansResponse(string correlationId)
    {
        CorrelationId = correlationId;
    }

    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
