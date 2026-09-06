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

public static class SubscriptionPlansListEndpoint
{
    public static void AddSubscriptionPlansListRoute(this IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", HandleAsync)
            .Produces<SubscriptionPlansListResponse>()
            .WithName("GetSubscriptionPlans")
            .WithTags("SubscriptionEndpoints");
    }

    private static async Task<IResult> HandleAsync(IMaxioBillingService billingService)
    {
        var response = new SubscriptionPlansListResponse();

        try
        {
            var plans = await billingService.GetAvailablePlansAsync();
            response.Plans = plans.Select(p => new SubscriptionPlanDto
            {
                Id = p.Id,
                Name = p.Name,
                Handle = p.Handle,
                Description = p.Description,
                Price = p.PriceInCents ?? 0
            }).ToList();

            response.Success = true;
        }
        catch (Exception ex)
        {
            response.Success = false;
            response.Error = ex.Message;
            return Results.BadRequest(response);
        }

        return Results.Ok(response);
    }
}

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
}

public class SubscriptionPlansListResponse : BaseResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
    public bool Success { get; set; }
    public string? Error { get; set; }
}
