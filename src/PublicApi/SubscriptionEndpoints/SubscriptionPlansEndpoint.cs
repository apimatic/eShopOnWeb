using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlansEndpoint : IEndpoint<IResult, SubscriptionPlansRequest, MaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (MaxioSubscriptionService svc) => await HandleAsync(new SubscriptionPlansRequest(), svc))
            .Produces<List<SubscriptionPlanResponse>>()
            .RequireAuthorization()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscriptionPlansRequest request, MaxioSubscriptionService service)
    {
        var products = await service.ListPlansAsync(request.CancellationToken);
        var result = products.Select(p => new SubscriptionPlanResponse
        {
            Id = p.Product?.Id ?? 0,
            Handle = p.Product?.Name ?? p.Product?.Handle ?? "",
            Name = p.Product?.Name ?? "",
            Price = p.Product?.ProductPricePointName ?? p.Product?.PriceInCents?.ToString() ?? ""
        }).ToList();
        return Results.Ok(result);
    }
}
