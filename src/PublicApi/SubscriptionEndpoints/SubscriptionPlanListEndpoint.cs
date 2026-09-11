using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;
using Microsoft.eShopWeb.PublicApi.Services;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanListRequest : BaseRequest { }
public class SubscriptionPlanListResponse : BaseResponse
{
    public SubscriptionPlanListResponse(Guid correlationId) : base(correlationId) { }
    public List<PlanDto> Plans { get; set; } = new();
}
public class PlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";
}

public class SubscriptionPlanListEndpoint : IEndpoint<IResult, SubscriptionPlanListRequest, Microsoft.eShopWeb.PublicApi.Services.IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioSubscriptionService svc) =>
            {
                var resp = new SubscriptionPlanListResponse(Guid.NewGuid());
                resp.Plans = new()
                {
                    new PlanDto { Handle = "eshop-pro", Name = "Pro Plan", Price = 299m },
                    new PlanDto { Handle = "basic-plan", Name = "Basic Plan", Price = 29m }
                };
                return Results.Ok(resp);
            })
            .Produces<SubscriptionPlanListResponse>()
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(SubscriptionPlanListRequest request, IMaxioSubscriptionService svc)
    {
        var resp = new SubscriptionPlanListResponse(request.CorrelationId());
        resp.Plans = new()
        {
            new PlanDto { Handle = "eshop-pro", Name = "Pro Plan", Price = 299m },
            new PlanDto { Handle = "basic-plan", Name = "Basic Plan", Price = 29m }
        };
        return Results.Ok(resp);
    }
}
