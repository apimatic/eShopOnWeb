using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Interval { get; set; } = string.Empty;
}

public class ListSubscriptionPlansResponse : Microsoft.eShopWeb.PublicApi.BaseResponse
{
    public ListSubscriptionPlansResponse() { }
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId) { }
    public List<PlanDto> Plans { get; set; } = new();
}

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, ListSubscriptionPlansRequest, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioBillingService service) =>
            {
                return await HandleAsync(new ListSubscriptionPlansRequest(), service);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .RequireAuthorization()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListSubscriptionPlansRequest request, IMaxioBillingService service)
    {
        var plans = await service.GetPlansAsync();
        var response = new ListSubscriptionPlansResponse(request.CorrelationId())
        {
            Plans = plans.Select(p => new PlanDto
            {
                Handle = p.Handle,
                Name = p.Name,
                Price = p.Price,
                Interval = p.Interval
            }).ToList()
        };
        return Results.Ok(response);
    }
}
