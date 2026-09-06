using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint
{
    public static void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            HandleAsync)
           .Produces<ListSubscriptionPlansResponse>()
           .WithTags("SubscriptionEndpoints")
           .AllowAnonymous();
    }

    private static async Task<IResult> HandleAsync(IMaxioBillingService billingService)
    {
        try
        {
            var plans = await billingService.GetSubscriptionPlansAsync();
            var response = new ListSubscriptionPlansResponse();
            response.SubscriptionPlans.AddRange(plans.Select(p => new SubscriptionPlanDto
            {
                Id = p.Id,
                Handle = p.Handle,
                Name = p.Name,
                Description = p.Description,
                Price = p.PriceInCents.HasValue ? p.PriceInCents.Value / 100m : null,
                Interval = p.Interval
            }));
            return Results.Ok(response);
        }
        catch (HttpRequestException ex)
        {
            return Results.BadRequest(new { error = "Failed to connect to Maxio", details = ex.Message });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = "Error fetching subscription plans", details = ex.Message });
        }
    }
}
