using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using System.Collections.Generic;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanEndpoint : IEndpoint<IResult, BaseRequest, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", async (IMaxioBillingService svc) =>
        {
            var plans = await svc.GetSubscriptionPlansAsync();
            return Results.Ok(plans);
        })
        .Produces<List<SubscriptionPlanEndpoint.PlanDto>>()
        .RequireAuthorization()
        .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(BaseRequest request, IMaxioBillingService svc)
    {
        var plans = await svc.GetSubscriptionPlansAsync();
        return Results.Ok(plans);
    }

    public class PlanDto
    {
        public string Handle { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public string FamilyHandle { get; set; } = string.Empty;
    }
}
