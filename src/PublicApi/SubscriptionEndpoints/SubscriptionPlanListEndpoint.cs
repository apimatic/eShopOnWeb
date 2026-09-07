using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http;
using System.Linq;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class SubscriptionPlanListEndpoint
{
    public static void MapListSubscriptionPlans(this WebApplication app)
    {
        app.MapGet("api/subscription-plans", ListSubscriptionPlans)
            .WithName("ListSubscriptionPlans")
            .Produces<SubscriptionPlanListResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    private static async Task<IResult> ListSubscriptionPlans(MaxioSubscriptionService service)
    {
        try
        {
            var plans = await service.GetSubscriptionPlansAsync();
            var response = new SubscriptionPlanListResponse(Guid.NewGuid())
            {
                Plans = plans.ConvertAll(p => new SubscriptionPlanDto
                {
                    Id = p.Id,
                    Handle = p.Handle,
                    Name = p.Name,
                    Price = p.Price
                })
            };
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
}

public class SubscriptionPlanListResponse : BaseResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();

    public SubscriptionPlanListResponse(Guid correlationId) : base(correlationId)
    {
    }
}
