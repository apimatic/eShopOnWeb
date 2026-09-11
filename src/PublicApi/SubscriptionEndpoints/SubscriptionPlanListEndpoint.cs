using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanListEndpoint : IEndpoint<IResult>
{
    private readonly IMaxioClient _maxio;
    private readonly MaxioSettings _settings;

    public SubscriptionPlanListEndpoint(IMaxioClient maxio, IOptions<MaxioSettings> settings)
    {
        _maxio = maxio;
        _settings = settings.Value;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async () =>
            {
                return await HandleAsync();
            })
            .Produces<List<SubscriptionPlanDto>>()
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync()
    {
        var products = await _maxio.ListProductsAsync(_settings.ProductFamilyHandle);
        var familyHandle = _settings.ProductFamilyHandle;
        var plans = products
            .Where(p => p.Product.ProductFamily?.Handle == familyHandle || string.IsNullOrEmpty(familyHandle) || p.Product.ProductFamily?.Name?.Contains("subscribe") == true)
            .Select(p => new SubscriptionPlanDto
            {
                Id = p.Product.Id,
                Handle = p.Product.Handle,
                Name = p.Product.Name,
                Description = p.Product.Description,
                Price = p.Product.PriceInCents / 100m,
                Interval = p.Product.IntervalUnit,
                FamilyHandle = p.Product.ProductFamily?.Handle ?? string.Empty
            })
            .ToList();
        return Results.Ok(plans);
    }
}
