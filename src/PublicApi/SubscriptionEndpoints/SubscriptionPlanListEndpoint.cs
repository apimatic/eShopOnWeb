using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Enrollment;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanListEndpoint : IEndpoint<IResult, IMaxioService>
{
    private readonly IHttpContextAccessor _http;

    public SubscriptionPlanListEndpoint(IHttpContextAccessor http)
    {
        _http = http;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioService maxio) =>
            {
                return await HandleAsync(maxio);
            })
           .Produces<List<SubscriptionPlanDto>>()
           .WithTags("SubscriptionEndpoints")
           .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(IMaxioService maxio)
    {
        var family = await maxio.ListFamilyProductsAsync();
        if (family == null || family["items"] is not System.Text.Json.Nodes.JsonArray items)
            return Results.Ok(new List<SubscriptionPlanDto>());

        var result = new List<SubscriptionPlanDto>();
        foreach (var item in items)
        {
            if (item?["product"] is not System.Text.Json.Nodes.JsonObject prod) continue;
            result.Add(new SubscriptionPlanDto
            {
                Handle = prod["handle"]?.GetValue<string>() ?? string.Empty,
                Name = prod["name"]?.GetValue<string>() ?? string.Empty,
                PriceInCents = prod["price_in_cents"]?.GetValue<int>() ?? 0,
                IntervalUnit = prod["interval_unit"]?.GetValue<string>() ?? string.Empty,
                Interval = prod["interval"]?.GetValue<int>() ?? 0
            });
        }
        return Results.Ok(result);
    }
}
