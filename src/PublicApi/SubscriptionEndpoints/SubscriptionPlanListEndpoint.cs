using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanListEndpoint : IEndpoint<IResult, object, ISubscriptionService>
{
    private readonly IHttpContextAccessor _accessor;
    public SubscriptionPlanListEndpoint(IHttpContextAccessor accessor) => _accessor = accessor;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (ISubscriptionService svc) => await HandleAsync(new object(), svc))
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(object request, ISubscriptionService service)
    {
        var plans = await service.GetSubscriptionPlansAsync();
        var response = new ListSubscriptionPlansResponse();
        response.Plans.AddRange(plans.Select(p => new SubscriptionPlanDto
        {
            Handle = p.Handle,
            Name = p.Name,
            Price = p.Price,
            PriceUnit = p.PriceUnit
        }));
        return Results.Ok(response);
    }
}
