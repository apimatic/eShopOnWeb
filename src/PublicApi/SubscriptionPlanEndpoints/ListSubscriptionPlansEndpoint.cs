using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

/// <summary>
/// Lists the subscription plans (Maxio products) available in the configured product family
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, ListSubscriptionPlansRequest>
{
    private readonly IMaxioClient _maxioClient;
    private readonly MaxioSettings _maxioSettings;

    public ListSubscriptionPlansEndpoint(IMaxioClient maxioClient, IOptions<MaxioSettings> maxioSettings)
    {
        _maxioClient = maxioClient;
        _maxioSettings = maxioSettings.Value;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CancellationToken cancellationToken) =>
            {
                return await HandleAsync(new ListSubscriptionPlansRequest(), cancellationToken);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionPlanEndpoints");
    }

    public Task<IResult> HandleAsync(ListSubscriptionPlansRequest request)
    {
        return HandleAsync(request, CancellationToken.None);
    }

    public async Task<IResult> HandleAsync(ListSubscriptionPlansRequest request, CancellationToken cancellationToken)
    {
        var response = new ListSubscriptionPlansResponse(request.CorrelationId());

        var products = await _maxioClient.ListProductsAsync(cancellationToken);

        response.Plans = products
            .Where(p => p.ArchivedAt is null)
            .Where(p => string.Equals(p.ProductFamily?.Handle, _maxioSettings.ProductFamilyHandle, System.StringComparison.OrdinalIgnoreCase))
            .Select(p => new SubscriptionPlanDto
            {
                Id = p.Id,
                Handle = p.Handle ?? string.Empty,
                Name = p.Name,
                Description = p.Description,
                PriceInCents = p.PriceInCents,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit
            })
            .ToList();

        return Results.Ok(response);
    }
}
