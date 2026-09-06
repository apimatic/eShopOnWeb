using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsEndpoint : IEndpoint<IResult>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (ClaimsPrincipal user, IMaxioService maxioService, UserManager<ApplicationUser> userManager) =>
            {
                return await ListMySubscriptionsHandler(user, maxioService, userManager);
            })
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync()
    {
        throw new NotImplementedException();
    }

    private static async Task<IResult> ListMySubscriptionsHandler(ClaimsPrincipal user, IMaxioService maxioService, UserManager<ApplicationUser> userManager)
    {
        var userName = ClaimsUtility.GetUserIdFromClaims(user);
        var appUser = await userManager.FindByNameAsync(userName);
        if (appUser == null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var customer = await maxioService.GetOrCreateCustomerAsync(
                appUser.Id,
                appUser.Email ?? "",
                appUser.Email?.Split('@')[0] ?? "User",
                "");

            var subscriptions = await maxioService.GetCustomerSubscriptionsAsync(customer.Id);

            var response = subscriptions.Select(s => new SubscriptionDto
            {
                Id = s.Id,
                State = s.State,
                ProductHandle = s.ProductHandle,
                ProductName = s.ProductName,
                Price = s.PriceInCents / 100m,
                NextBillingAt = s.NextBillingAt,
                CreatedAt = s.CreatedAt
            }).ToList();

            return Results.Ok(new { subscriptions = response });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = $"Failed to retrieve subscriptions: {ex.Message}" });
        }
    }
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string? State { get; set; }
    public string? ProductHandle { get; set; }
    public string? ProductName { get; set; }
    public decimal Price { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public DateTime? CreatedAt { get; set; }
}
