using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanListEndpoint : IEndpoint<IResult, MaxioApiClient>
{
    private readonly IOptions<MaxioOptions> _options;

    public SubscriptionPlanListEndpoint(IOptions<MaxioOptions> options)
    {
        _options = options;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (MaxioApiClient maxioClient) =>
            {
                return await HandleAsync(maxioClient);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MaxioApiClient maxioClient)
    {
        var response = new ListSubscriptionPlansResponse();
        var familyHandle = _options.Value.ProductFamilyHandle;

        var products = await maxioClient.ListProductsForFamilyAsync(familyHandle);

        if (products != null)
        {
            response.Plans = products
                .Where(i => i.Product != null)
                .Select(i => new SubscriptionPlanDto
                {
                    Id = i.Product!.Id,
                    Name = i.Product.Name,
                    Handle = i.Product.Handle,
                    Description = i.Product.Description,
                    Price = i.Product.PriceInCents / 100m,
                    IntervalUnit = i.Product.IntervalUnit,
                    Interval = i.Product.Interval,
                    Taxable = i.Product.Taxable,
                    RequireCreditCard = i.Product.RequireCreditCard
                })
                .ToList();
        }

        return Results.Ok(response);
    }
}
