using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanListRequest : BaseRequest { }

public class SubscriptionPlanListResponse : BaseResponse
{
    public SubscriptionPlanListResponse() { }
    public SubscriptionPlanListResponse(Guid correlationId) : base(correlationId) { }
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public bool RequireCreditCard { get; set; }
}

public class SubscriptionPlanListEndpoint : IEndpoint<IResult, SubscriptionPlanListRequest, Maxio.IMaxioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", async (Maxio.IMaxioService maxioService) =>
        {
            return await HandleAsync(new SubscriptionPlanListRequest(), maxioService);
        })
        .Produces<SubscriptionPlanListResponse>()
        .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscriptionPlanListRequest request, Maxio.IMaxioService maxioService)
    {
        var plans = await maxioService.ListPlansAsync();

        var response = new SubscriptionPlanListResponse(request.CorrelationId());
        response.Plans.AddRange(plans.Select(p => new SubscriptionPlanDto
        {
            Id = p.Id,
            Name = p.Name,
            Handle = p.Handle,
            Description = p.Description,
            Price = p.Price,
            Interval = p.Interval,
            IntervalUnit = p.IntervalUnit,
            RequireCreditCard = p.RequireCreditCard
        }));

        return Results.Ok(response);
    }
}
