using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

public class SubscriptionPlanListEndpoint : IEndpoint<IResult, SubscriptionPlanListRequest, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioBillingService svc) => await HandleAsync(new SubscriptionPlanListRequest(), svc))
            .Produces<SubscriptionPlanListResponse>()
            .WithTags("SubscriptionPlanEndpoints")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(SubscriptionPlanListRequest request, IMaxioBillingService svc)
    {
        var plans = await svc.GetSubscriptionPlansAsync();
        var response = new SubscriptionPlanListResponse(request.CorrelationId());
        foreach (var p in plans)
        {
            response.Plans.Add(new SubscriptionPlanDto
            {
                Id = p.Id,
                Name = p.Name,
                Handle = p.Handle,
                Price = p.PriceInCents / 100m,
                Interval = p.IntervalUnit,
                Taxable = p.Taxable,
                RequiresCreditCard = p.RequiresCreditCard
            });
        }
        return Results.Ok(response);
    }
}

public class SubscriptionPlanListRequest : BaseRequest
{
    public SubscriptionPlanListRequest() {}
}

public class SubscriptionPlanListResponse : BaseResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
    public SubscriptionPlanListResponse(Guid correlationId)  { }
}
