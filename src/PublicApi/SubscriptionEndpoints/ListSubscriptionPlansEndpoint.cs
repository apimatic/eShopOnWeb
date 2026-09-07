using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult>
{
    private readonly MaxioApiClient _maxioClient;

    public ListSubscriptionPlansEndpoint(MaxioApiClient maxioClient)
    {
        _maxioClient = maxioClient;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/subscription-plans",
            async () =>
            {
                return await HandleAsync();
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithName("ListSubscriptionPlans")
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync()
    {
        var response = new ListSubscriptionPlansResponse();

        var products = await _maxioClient.GetAsync<List<MaxioProductWrapper>>("/products.json");
        if (products == null || products.Count == 0)
        {
            return Results.Ok(response);
        }

        response.Plans = products
            .Where(w => w.Product != null)
            .Select(w => w.Product)
            .Where(p => p.ProductFamily?.Handle == "eshop-subscribe")
            .Select(p => new SubscriptionPlanDto
            {
                Id = p.Id,
                Name = p.Name,
                Handle = p.Handle,
                Description = p.Description ?? string.Empty,
                PriceInDollars = p.PriceInCents.HasValue ? p.PriceInCents.Value / 100m : 0,
                IntervalDays = p.Interval ?? 1,
                IntervalUnit = p.IntervalUnit ?? "month"
            })
            .ToList();

        return Results.Ok(response);
    }

    public class ListSubscriptionPlansResponse
    {
        public List<SubscriptionPlanDto> Plans { get; set; } = new();
    }

    private class MaxioProductWrapper
    {
        [JsonPropertyName("product")]
        public MaxioProduct? Product { get; set; }
    }

    private class MaxioProduct
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("handle")]
        public string Handle { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("price_in_cents")]
        public int? PriceInCents { get; set; }

        [JsonPropertyName("interval")]
        public int? Interval { get; set; }

        [JsonPropertyName("interval_unit")]
        public string? IntervalUnit { get; set; }

        [JsonPropertyName("product_family")]
        public MaxioProductFamily? ProductFamily { get; set; }
    }

    private class MaxioProductFamily
    {
        [JsonPropertyName("handle")]
        public string Handle { get; set; } = string.Empty;
    }
}
