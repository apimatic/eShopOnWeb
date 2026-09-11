using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanListEndpoint : IEndpoint<IResult, IMaxioService, MaxioSettings>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioService service, MaxioSettings settings) =>
            {
                return await HandleAsync(service, settings);
            })
            .Produces<SubscriptionPlanListResponse>()
           .RequireAuthorization()
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioService service, MaxioSettings settings)
    {
        var family = !string.IsNullOrEmpty(settings.ProductFamilyHandle) ? settings.ProductFamilyHandle : "eshop-subscribe";
        var products = await service.ListPlansAsync(family);
        var response = new SubscriptionPlanListResponse();
        response.Plans.AddRange(products.Select(p => new SubscriptionPlanDto
        {
            Id = p.id,
            Name = p.name,
            Handle = p.handle,
            Price = p.price_in_cents / 100m,
            Interval = $"{p.interval} {p.interval_unit}"
        }));
        return Results.Ok(response);
    }
}
