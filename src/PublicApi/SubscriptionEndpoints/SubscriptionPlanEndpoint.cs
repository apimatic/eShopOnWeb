using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanEndpoint : IEndpoint<IResult, SubscriptionPlanRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", async (ISubscriptionService svc) =>
        {
            return await HandleAsync(new SubscriptionPlanRequest(), svc);
        })
        .Produces<SubscriptionPlanResponse>()
        .WithTags("SubscriptionEndpoints")
        .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(SubscriptionPlanRequest request, ISubscriptionService svc)
    {
        var response = new SubscriptionPlanResponse(request.CorrelationId());
        response.Plans = await svc.GetPlansAsync();
        return Results.Ok(response);
    }
}

public class SubscriptionPlanRequest : BaseRequest { }

public class SubscriptionPlanResponse : BaseResponse
{
    public SubscriptionPlanResponse(Guid correlationId) : base(correlationId) { }
    public IReadOnlyList<SubscriptionPlanDto> Plans { get; set; } = new List<SubscriptionPlanDto>();
}
