using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, ListSubscriptionPlansRequest, IMaxioClient>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioClient maxioClient) =>
            {
                return await HandleAsync(new ListSubscriptionPlansRequest(), maxioClient);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListSubscriptionPlansRequest request, IMaxioClient maxioClient)
    {
        var response = new ListSubscriptionPlansResponse(request.CorrelationId());

        var products = await maxioClient.ListProductsAsync();

        response.Plans = products
            .Where(p => p.ArchivedAt == null)
            .Select(p => new SubscriptionPlanDto
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
            })
            .ToList();

        return Results.Ok(response);
    }
}
