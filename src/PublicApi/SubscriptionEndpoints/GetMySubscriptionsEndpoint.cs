using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class GetMySubscriptionsEndpoint
{
    public static void MapGetMySubscriptions(this WebApplication app)
    {
        app.MapGet("api/my-subscriptions", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<GetMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("GetMySubscriptions");
    }

    private static async Task<IResult> HandleAsync(IMaxioApiClient maxioClient, UserManager<ApplicationUser> userManager, HttpContext context)
    {
        var userId = context.User.FindFirst(ClaimTypes.Name)?.Value;
        var response = new GetMySubscriptionsResponse();

        if (string.IsNullOrEmpty(userId))
        {
            response.Success = false;
            response.Message = "User not authenticated";
            return Results.Unauthorized();
        }

        try
        {
            var user = await userManager.FindByNameAsync(userId);
            if (user == null)
            {
                response.Success = false;
                response.Message = "User not found";
                return Results.NotFound(response);
            }

            var customer = await maxioClient.FindCustomerByReferenceAsync(user.Id);
            if (customer == null)
            {
                response.Success = true;
                response.Subscriptions = new();
                return Results.Ok(response);
            }

            var subscriptions = await maxioClient.ListCustomerSubscriptionsAsync(customer.Id);
            response.Subscriptions = subscriptions.Select(s => new SubscriptionSummaryDto
            {
                Id = s.Id,
                ProductName = s.ProductName,
                ProductHandle = s.ProductHandle,
                State = s.State,
                Price = s.GetPrice(),
                NextBillingAt = s.NextBillingAt,
                CreatedAt = s.CreatedAt
            }).ToList();
            response.Success = true;

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            response.Success = false;
            response.Message = $"Failed to retrieve subscriptions: {ex.Message}";
            return Results.BadRequest(response);
        }
    }
}

public class GetMySubscriptionsResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public List<SubscriptionSummaryDto> Subscriptions { get; set; } = new();
}

public class SubscriptionSummaryDto
{
    public int Id { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public DateTime? CreatedAt { get; set; }
}
