using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class SubscriptionPlansListEndpoint
{
    public static void MapSubscriptionPlansEndpoint(this WebApplication app)
    {
        app.MapGet("/api/subscription-plans", Handle)
            .RequireAuthorization()
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("ListSubscriptionPlans");
    }

    private static async Task<IResult> Handle(IMaxioSubscriptionService subscriptionService)
    {
        var response = new ListSubscriptionPlansResponse();

        try
        {
            var plans = await subscriptionService.GetProductsAsync();
            response.Plans.AddRange(plans.Select(p => new SubscriptionPlanDto
            {
                Id = p.Id,
                Handle = p.Handle,
                Name = p.Name,
                Description = p.Description,
                Price = p.Price
            }));

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = "Failed to retrieve subscription plans", details = ex.Message });
        }
    }
}

public class ListSubscriptionPlansResponse : BaseResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
