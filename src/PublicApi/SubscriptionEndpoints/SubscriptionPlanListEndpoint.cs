using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanListEndpoint : IEndpoint<IResult>
{
    private readonly IMaxioApiClient _maxioClient;
    private readonly MaxioSettings _settings;

    public SubscriptionPlanListEndpoint(
        IMaxioApiClient maxioClient,
        IOptions<MaxioSettings> settings)
    {
        _maxioClient = maxioClient;
        _settings = settings.Value;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async () =>
            {
                return await HandleAsync();
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        var response = new ListSubscriptionPlansResponse();

        var products = await _maxioClient.ListProductsAsync(_settings.ProductFamilyHandle);

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
                ProductFamilyHandle = p.ProductFamily?.Handle ?? _settings.ProductFamilyHandle
            })
            .ToList();

        return Results.Ok(response);
    }
}
