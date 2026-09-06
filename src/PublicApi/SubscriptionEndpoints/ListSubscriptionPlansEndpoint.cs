using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint
{
    public static void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioSubscriptionService subscriptionService) =>
            {
                var plans = await subscriptionService.GetAvailablePlansAsync();
                var response = new ListSubscriptionPlansResponse
                {
                    Plans = plans.Select(p => new SubscriptionPlanDto
                    {
                        Id = p.Id,
                        Name = p.Name,
                        Handle = p.Handle,
                        Description = p.Description,
                        PriceInCents = p.PriceInCents,
                        PriceInDollars = p.PriceInCents / 100m,
                        Interval = p.Interval,
                        IntervalUnit = p.IntervalUnit
                    }).ToList()
                };
                return Results.Ok(response);
            })
            .Produces<ListSubscriptionPlansResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints")
            .WithName("ListSubscriptionPlans");
    }
}

public class SubscriptionPlanDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("priceInCents")]
    public long PriceInCents { get; set; }

    [JsonPropertyName("priceInDollars")]
    public decimal PriceInDollars { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("intervalUnit")]
    public string IntervalUnit { get; set; } = "";
}

public class ListSubscriptionPlansResponse
{
    [JsonPropertyName("plans")]
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
