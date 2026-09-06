using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansRequest { }

/// <summary>
/// List Subscription Plans
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, ListSubscriptionPlansRequest, IMaxioApiService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioApiService maxioService) =>
            {
                return await HandleAsync(new ListSubscriptionPlansRequest(), maxioService);
            })
           .Produces<ListSubscriptionPlansResponse>()
           .WithName("ListSubscriptionPlans")
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListSubscriptionPlansRequest request, IMaxioApiService maxioService)
    {
        var response = new ListSubscriptionPlansResponse();

        try
        {
            var plans = await maxioService.ListSubscriptionPlansAsync();

            response.SubscriptionPlans.AddRange(plans.Select(p => new SubscriptionPlanDto
            {
                Id = p.Id,
                Handle = p.Handle,
                Name = p.Name,
                Price = FormatPrice(p.PriceInCents),
                Description = p.Description,
                BillingCycle = FormatBillingCycle(p.Interval, p.IntervalUnit)
            }));

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private string FormatPrice(long priceCents)
    {
        return $"${priceCents / 100m:F2}";
    }

    private string FormatBillingCycle(int interval, string unit)
    {
        return interval == 1 ? $"per {unit}" : $"every {interval} {unit}s";
    }
}

public class ListSubscriptionPlansResponse
{
    public List<SubscriptionPlanDto> SubscriptionPlans { get; } = new();
}
