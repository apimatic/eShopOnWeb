using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanListEndpoint : IEndpoint<IResult, SubscriptionPlanListRequest, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioBillingService svc) => await HandleAsync(new SubscriptionPlanListRequest(), svc))
            .Produces<SubscriptionPlanListResponse>()
            .RequireAuthorization()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscriptionPlanListRequest request, IMaxioBillingService svc)
    {
        var items = await svc.ListProductsAsync();
        var response = new SubscriptionPlanListResponse(request.CorrelationId())
        {
            Plans = new List<PlanDto>()
        };
        foreach (var p in items)
        {
            response.Plans.Add(new PlanDto
            {
                Id = p.Id,
                Handle = p.Handle,
                Name = p.Name,
                PriceInCents = (long)p.PriceInCents,
                Price = (p.PriceInCents / 100m).ToString("F2")
            });
        }
        return Results.Ok(response);
    }
}

public class SubscriptionPlanListRequest : BaseRequest { }

public class SubscriptionPlanListResponse : BaseResponse
{
    public SubscriptionPlanListResponse(Guid correlationId) : base(correlationId) { }
    public List<PlanDto> Plans { get; set; } = new();
}

public class PlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public string Price { get; set; } = string.Empty;
}
