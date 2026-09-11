using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

public class SubscriptionPlanListEndpoint : IEndpoint<IResult>
{
    private readonly IMaxioClient _maxioClient;
    private readonly MaxioOptions _maxioOptions;

    public SubscriptionPlanListEndpoint(IMaxioClient maxioClient, IOptions<MaxioOptions> maxioOptions)
    {
        _maxioClient = maxioClient;
        _maxioOptions = maxioOptions.Value;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async () =>
            {
                return await HandleAsync();
            })
           .Produces<ListSubscriptionPlansResponse>()
           .WithTags("SubscriptionPlanEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        var response = new ListSubscriptionPlansResponse();

        var products = await _maxioClient.ListProductsAsync(_maxioOptions.ProductFamilyHandle);

        response.Plans.AddRange(products.Select(p => new SubscriptionPlanDto
        {
            Id = p.Id,
            Name = p.Name,
            Handle = p.Handle,
            Description = p.Description,
            Price = p.PriceInCents / 100m,
            PriceDisplay = $"${p.PriceInCents / 100m:F2}/{(p.IntervalUnit == "month" ? "mo" : "day")}",
            Interval = p.Interval,
            IntervalUnit = p.IntervalUnit,
            IntervalDisplay = p.Interval == 1
                ? $"per {p.IntervalUnit}"
                : $"every {p.Interval} {p.IntervalUnit}s",
            RequireCreditCard = p.RequireCreditCard,
            Taxable = p.Taxable,
            ProductFamilyHandle = p.ProductFamily?.Handle ?? string.Empty
        }));

        return Results.Ok(response);
    }
}
