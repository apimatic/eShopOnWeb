using System;
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

public class CreateSubscriptionEndpoint : IEndpoint<IResult>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, ClaimsPrincipal user, IMaxioService maxioService, UserManager<ApplicationUser> userManager) =>
            {
                return await CreateSubscriptionHandler(request, user, maxioService, userManager);
            })
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync()
    {
        throw new NotImplementedException();
    }

    private static async Task<IResult> CreateSubscriptionHandler(
        CreateSubscriptionRequest request,
        ClaimsPrincipal user,
        IMaxioService maxioService,
        UserManager<ApplicationUser> userManager)
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
                appUser.Id.ToString(),
                appUser.Email ?? "",
                appUser.Email?.Split('@')[0] ?? "User",
                "");

            var subscription = await maxioService.CreateSubscriptionAsync(customer.Id, request.ProductHandle);

            var response = new CreateSubscriptionResponse
            {
                SubscriptionId = subscription.Id,
                State = subscription.State,
                ProductHandle = subscription.ProductHandle,
                ProductName = subscription.ProductName,
                Price = subscription.PriceInCents / 100m,
                NextBillingAt = subscription.NextBillingAt,
                CreatedAt = subscription.CreatedAt
            };

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = $"Failed to create subscription: {ex.Message}" });
        }
    }
}

public class CreateSubscriptionRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse
{
    public int SubscriptionId { get; set; }
    public string? State { get; set; }
    public string? ProductHandle { get; set; }
    public string? ProductName { get; set; }
    public decimal Price { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public DateTime? CreatedAt { get; set; }
}
