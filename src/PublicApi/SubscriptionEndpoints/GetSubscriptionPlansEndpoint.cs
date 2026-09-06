using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanResponse
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal PriceInDollars { get; set; }
}

public class ListSubscriptionPlansResponse
{
    public List<SubscriptionPlanResponse> Plans { get; set; } = new();
}

public class GetSubscriptionPlansEndpoint : IEndpoint<IResult, IMaxioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioService maxioService) =>
            {
                return await HandleAsync(maxioService);
            })
           .WithName("GetSubscriptionPlans")
           .Produces<ListSubscriptionPlansResponse>()
           .WithTags("SubscriptionEndpoints")
           .WithSummary("List available subscription plans")
           .WithDescription("Returns a list of available subscription plans from Maxio");
    }

    public async Task<IResult> HandleAsync(IMaxioService maxioService)
    {
        var plans = await maxioService.GetSubscriptionPlansAsync();
        var response = new ListSubscriptionPlansResponse();

        response.Plans.AddRange(plans.Select(p => new SubscriptionPlanResponse
        {
            Handle = p.Handle,
            Name = p.Name,
            Description = p.Description,
            PriceInDollars = p.PriceInDollars
        }));

        return Results.Ok(response);
    }
}
