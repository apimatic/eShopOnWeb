using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List available subscription plans from Maxio
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult>
{
    private readonly MaxioClient _maxioClient;
    private readonly MaxioOptions _options;

    public SubscriptionPlanListEndpoint(MaxioClient maxioClient, IOptions<MaxioOptions> options)
    {
        _maxioClient = maxioClient;
        _options = options.Value;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (HttpContext httpContext) =>
            {
                return await HandleAsync();
            })
            .Produces<ListSubscriptionPlansResponse>()
            .RequireAuthorization(JwtBearerDefaults.AuthenticationScheme)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        var response = new ListSubscriptionPlansResponse();

        var products = await _maxioClient.ListProductsAsync();

        var plans = products
            .Where(p => p.ArchivedAt == null)
            .Where(p => p.ProductFamily?.Handle == _options.ProductFamilyHandle)
            .Select(p => new SubscriptionPlanDto
            {
                Id = p.Id,
                Name = p.Name,
                Handle = p.Handle,
                Description = p.Description,
                PriceInDollars = p.PriceInCents / 100m,
                IntervalUnit = p.IntervalUnit,
                Interval = p.Interval,
                RequireCreditCard = p.RequireCreditCard,
                Taxable = p.Taxable,
                ProductFamilyHandle = p.ProductFamily?.Handle
            })
            .ToList();

        response.Plans.AddRange(plans);
        return Results.Ok(response);
    }
}
