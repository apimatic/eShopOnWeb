using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List available subscription plans from Maxio
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult, IMaxioClient>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", async (IMaxioClient maxioClient) =>
        {
            return await HandleAsync(maxioClient);
        })
        .Produces<SubscriptionPlanListResponse>()
        .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioClient maxioClient)
    {
        var response = new SubscriptionPlanListResponse();

        var products = await maxioClient.GetProductsAsync();

        response.Plans = products.Select(p => new SubscriptionPlanDto
        {
            Id = p.Id,
            Name = p.Name,
            Handle = p.Handle,
            Description = p.Description,
            Price = p.PriceInCents / 100m,
            IntervalUnit = p.IntervalUnit,
            Interval = p.Interval,
            RequireCreditCard = p.RequireCreditCard,
            ProductFamilyHandle = p.ProductFamily?.Handle ?? string.Empty
        }).ToList();

        return Results.Ok(response);
    }
}
