using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class ListSubscriptionPlansEndpoint
{
    public static void MapListSubscriptionPlansEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", ListPlans)
            .RequireAuthorization()
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    private static async Task<IResult> ListPlans(IMaxioSubscriptionService maxioService)
    {
        var request = new ListSubscriptionPlansRequest();
        var response = new ListSubscriptionPlansResponse(request.CorrelationId());

        var plans = await maxioService.GetAvailablePlansAsync();
        response.Plans = plans.Select(p => new PlanDto
        {
            Id = p.Id,
            Handle = p.Handle,
            Name = p.Name,
            Price = p.Price,
            Description = p.Description
        }).ToList();

        return Results.Ok(response);
    }
}

public class ListSubscriptionPlansRequest : BaseRequest
{
}

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse() { }
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId) { }

    public List<PlanDto> Plans { get; set; } = new();
}

public class PlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Description { get; set; } = string.Empty;
}
