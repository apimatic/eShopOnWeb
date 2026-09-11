using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanListEndpoint : IEndpoint<IResult, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = "Bearer")]
            async (ClaimsPrincipal user, IMaxioBillingService svc) =>
            {
                return await HandleAsync(svc);
            })
            .Produces<SubscriptionPlanListResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioBillingService svc)
    {
        var plans = await svc.GetPlansAsync();
        var resp = new SubscriptionPlanListResponse
        {
            Plans = plans.Select(p => new SubscriptionPlanDto
            {
                Id = p.Id,
                Name = p.Name,
                Handle = p.Handle,
                PriceInCents = p.PriceInCents,
                IntervalUnit = p.IntervalUnit
            }).ToList()
        };
        return Results.Ok(resp);
    }
}
