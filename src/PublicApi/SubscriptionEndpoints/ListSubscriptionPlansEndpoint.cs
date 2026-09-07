using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class ListSubscriptionPlansEndpoint
{
    public static void MapListSubscriptionPlans(this WebApplication app)
    {
        app.MapGet("api/subscription-plans", HandleAsync)
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("ListSubscriptionPlans");
    }

    private static async Task<IResult> HandleAsync(IMaxioApiClient maxioClient, IOptionsSnapshot<MaxioSettings> optionsSnapshot, HttpContext context)
    {
        var settings = optionsSnapshot.Value;
        var response = new ListSubscriptionPlansResponse();

        if (string.IsNullOrEmpty(settings.ProductFamilyHandle))
        {
            response.Success = false;
            response.Message = "Product family handle is not configured";
            return Results.BadRequest(response);
        }

        try
        {
            var products = await maxioClient.ListProductsAsync(settings.ProductFamilyHandle);
            response.Plans = products.Select(p => new SubscriptionPlanDto
            {
                Id = p.Id,
                Handle = p.Handle,
                Name = p.Name,
                Description = p.Description ?? string.Empty,
                Price = p.GetPrice(),
                BillingInterval = $"{p.Interval} {p.IntervalUnit}"
            }).ToList();
            response.Success = true;
        }
        catch (Exception ex)
        {
            response.Success = false;
            response.Message = $"Failed to retrieve subscription plans: {ex.Message}";
            return Results.BadRequest(response);
        }

        return Results.Ok(response);
    }
}

public class ListSubscriptionPlansResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string BillingInterval { get; set; } = string.Empty;
}
