using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanEndpoint : IEndpoint<IResult, SubscriptionPlanRequest, IMaxioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", async (IMaxioService service) =>
        {
            return await HandleAsync(new SubscriptionPlanRequest(), service);
        })
        .Produces<List<SubscriptionPlanResponse>>()
        .WithTags("SubscriptionEndpoints")
        .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(SubscriptionPlanRequest request, IMaxioService service)
    {
        var products = await service.ListProductsAsync();
        var response = products.Select(p => new SubscriptionPlanResponse
        {
            Id = p.Id,
            Handle = p.Handle,
            Name = p.Name,
            PriceInCents = p.PriceInCents,
            Interval = p.Interval,
            IntervalUnit = p.IntervalUnit,
            Taxable = p.Taxable,
            RequireCreditCard = p.RequireCreditCard,
            Description = p.Description
        }).ToList();
        return Results.Ok(response);
    }
}

public class SubscriptionPlanRequest : BaseRequest
{
}
