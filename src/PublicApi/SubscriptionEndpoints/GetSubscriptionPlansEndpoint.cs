using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetSubscriptionPlansRequest : BaseRequest { }

public class PlanDto
{
    public string Handle { get; set; } = "";
    public string Name { get; set; } = "";
    public long PriceInCents { get; set; }
    public string PriceDisplay => $"${PriceInCents / 100.0:F2}";
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "month";
}

public class GetSubscriptionPlansResponse : BaseResponse
{
    public GetSubscriptionPlansResponse() { }
    public GetSubscriptionPlansResponse(Guid correlationId) : base(correlationId) { }
    public List<PlanDto> Plans { get; set; } = new();
}

public class GetSubscriptionPlansEndpoint : IEndpoint<IResult, GetSubscriptionPlansRequest, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)]
            async (IMaxioBillingService svc) =>
            {
                return await HandleAsync(new GetSubscriptionPlansRequest(), svc);
            })
            .Produces<GetSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(GetSubscriptionPlansRequest request, IMaxioBillingService svc)
    {
        var response = new GetSubscriptionPlansResponse(request.CorrelationId());
        var plans = await svc.GetPlansAsync();
        response.Plans = plans.Select(p => new PlanDto
        {
            Handle = p.Handle,
            Name = p.Name,
            PriceInCents = p.PriceInCents,
            Interval = p.Interval,
            IntervalUnit = p.IntervalUnit
        }).ToList();
        return Results.Ok(response);
    }
}
