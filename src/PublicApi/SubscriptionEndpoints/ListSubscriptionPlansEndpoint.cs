using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            ListSubscriptionPlans)
           .Produces<ListSubscriptionPlansResponse>()
           .WithName("ListSubscriptionPlans")
           .WithTags("Subscriptions")
;
    }

    private static async Task<IResult> ListSubscriptionPlans(IMaxioApiClient maxioApiClient, IOptions<MaxioSettings> maxioSettings)
    {
        var response = new ListSubscriptionPlansResponse();

        try
        {
            var maxioConfig = maxioSettings.Value;
            var plans = await maxioApiClient.ListProductsByFamilyAsync(maxioConfig.ProductFamilyHandle);

            response.Plans = plans.Select(p => new SubscriptionPlanDto
            {
                Id = p.Id,
                Name = p.Name,
                Handle = p.Handle,
                PricePerMonth = p.PriceInCents / 100m,
                Description = p.Description
            }).ToList();

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            response.Error = $"Failed to load subscription plans: {ex.Message}";
            return Results.BadRequest(response);
        }
    }
}

public class ListSubscriptionPlansResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
    public string? Error { get; set; }
}
