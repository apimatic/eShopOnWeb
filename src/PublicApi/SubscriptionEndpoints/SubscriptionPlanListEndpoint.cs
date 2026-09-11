using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanListEndpoint : IEndpoint<IResult, SubscriptionPlanListRequest, IMaxioService>
{
    private readonly IHttpContextAccessor _accessor;
    public SubscriptionPlanListEndpoint(IHttpContextAccessor accessor) => _accessor = accessor;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioService maxio) =>
            {
                return await HandleAsync(new SubscriptionPlanListRequest(), maxio);
            })
            .Produces<SubscriptionPlanListResponse>()
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(SubscriptionPlanListRequest request, IMaxioService maxio)
    {
        var response = new SubscriptionPlanListResponse(request.CorrelationId());
        var family = await maxio.GetProductFamilyAsync();
        if (family == null) return Results.Problem("Maxio family unavailable");

        var products = await maxio.GetProductsAsync();
        var plans = new List<SubscriptionPlanDto>();
        if (products != null)
        {
            foreach (var p in products)
            {
                if (p is JsonObject obj)
                {
                    var handle = obj["handle"]?.GetValue<string>() ?? obj["product_family"]? ["handle"]?.GetValue<string>();
                    // Return all products from family; filter if needed
                    plans.Add(new SubscriptionPlanDto
                    {
                        Handle = obj["handle"]?.GetValue<string>() ?? "",
                        Id = obj["id"]?.GetValue<int>() ?? 0,
                        Name = obj["name"]?.GetValue<string>() ?? obj["family"]?["name"]?.GetValue<string>() ?? "",
                        Price = obj["price_in_cents"] != null ? obj["price_in_cents"].GetValue<int>() / 100.0m : (obj["unit_price"]?.GetValue<decimal>() ?? 0),
                        ProductFamilyHandle = obj["product_family"]?["handle"]?.GetValue<string>() ?? ""
                    });
                }
            }
        }
        // Deduplicate by handle
        response.Plans = plans.GroupBy(x => x.Handle).Select(g => g.First()).ToList();
        return Results.Ok(response);
    }
}

public class SubscriptionPlanListResponse : BaseResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
    public SubscriptionPlanListResponse(Guid correlationId) : base(correlationId) { }
}

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string ProductFamilyHandle { get; set; } = string.Empty;
}
