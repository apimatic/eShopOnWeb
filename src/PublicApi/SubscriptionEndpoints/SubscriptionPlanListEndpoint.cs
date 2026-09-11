using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Services.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanListEndpoint : IEndpoint<IResult, SubscriptionPlanListRequest, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioBillingService service) =>
            {
                return await HandleAsync(new SubscriptionPlanListRequest(), service);
            })
            .Produces<SubscriptionPlanListResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscriptionPlanListRequest request, IMaxioBillingService service)
    {
        var response = new SubscriptionPlanListResponse(request.CorrelationId());
        var plans = await service.GetPlansAsync();
        response.Plans = plans.Select(p => new SubscriptionPlanDto
        {
            Id = p.Id,
            Handle = p.Handle,
            Name = p.Name,
            Price = p.Price,
            FamilyHandle = p.FamilyHandle
        }).ToList();
        return Results.Ok(response);
    }
}
