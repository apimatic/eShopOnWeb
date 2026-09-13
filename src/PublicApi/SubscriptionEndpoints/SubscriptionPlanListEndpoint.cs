using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List available subscription plans from Maxio.
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult, MaxioApiClient>
{
    private readonly MaxioConfiguration _config;

    public SubscriptionPlanListEndpoint(IOptions<MaxioConfiguration> config)
    {
        _config = config.Value;
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

        var products = await maxioClient.ListProductsAsync();
        if (products == null)
        {
            return Results.StatusCode(502);
        }

        var plans = products
            .Where(p => p.Product != null
                && p.Product.ProductFamily?.Handle == _config.ProductFamilyHandle
                && p.Product.ArchivedAt == null)
            .Select(p => new SubscriptionPlanDto
            {
                Id = p.Product!.Id,
                Name = p.Product.Name,
                Handle = p.Product.Handle,
                Description = p.Product.Description,
                Price = p.Product.PriceInCents / 100m,
                Interval = p.Product.IntervalUnit,
                IntervalCount = p.Product.Interval,
                RequireCreditCard = p.Product.RequireCreditCard,
                ProductFamilyHandle = p.Product.ProductFamily?.Handle
            })
            .ToList();

        response.SubscriptionPlans = plans;
        return Results.Ok(response);
    }
}

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId) { }
    public ListSubscriptionPlansResponse() { }

    public List<SubscriptionPlanDto> SubscriptionPlans { get; set; } = new();
}
