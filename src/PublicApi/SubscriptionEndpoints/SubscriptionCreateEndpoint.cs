using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class SubscriptionCreateEndpoint
{
    public static void AddSubscriptionCreateRoute(this IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", HandleAsync)
            .Produces<SubscriptionCreateResponse>()
            .WithName("CreateSubscription")
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        SubscriptionCreateRequest request,
        HttpContext httpContext,
        UserManager<ApplicationUser> userManager,
        IMaxioBillingService billingService)
    {
        var response = new SubscriptionCreateResponse();

        try
        {
            var userName = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
            if (string.IsNullOrEmpty(userName))
            {
                return Results.Unauthorized();
            }

            var user = await userManager.FindByNameAsync(userName);
            if (user is null)
            {
                return Results.NotFound("User not found");
            }

            if (user.MaxioCustomerId.HasValue)
            {
                return Results.BadRequest(new { error = "User already has an active subscription" });
            }

            var firstName = user.UserName?.Split(' ').FirstOrDefault() ?? "User";
            var lastName = user.UserName?.Split(' ').Skip(1).FirstOrDefault() ?? string.Empty;
            var email = user.Email ?? $"{user.Id}@eshop.local";

            var maxioCustomer = await billingService.GetOrCreateCustomerAsync(
                user.Id,
                email,
                firstName,
                lastName);

            if (maxioCustomer is null)
            {
                return Results.BadRequest(new { error = "Failed to create or retrieve Maxio customer" });
            }

            var subscription = await billingService.CreateSubscriptionAsync(
                maxioCustomer.Id,
                request.ProductHandle ?? "eshop-pro");

            user.MaxioCustomerId = maxioCustomer.Id;
            await userManager.UpdateAsync(user);

            response.Success = true;
            response.SubscriptionId = subscription.Id;
            response.CustomerId = maxioCustomer.Id;
            response.State = subscription.State;
            response.NextBillingDate = subscription.NextAssessmentAt;

            return Results.Created($"api/subscriptions/{subscription.Id}", response);
        }
        catch (Exception ex)
        {
            response.Success = false;
            response.Error = $"Failed to create subscription: {ex.Message}";
            return Results.BadRequest(response);
        }
    }
}

public class SubscriptionCreateRequest : BaseRequest
{
    public string? ProductHandle { get; set; }
}

public class SubscriptionCreateResponse : BaseResponse
{
    public bool Success { get; set; }
    public int SubscriptionId { get; set; }
    public int CustomerId { get; set; }
    public string? State { get; set; }
    public DateTime? NextBillingDate { get; set; }
    public string? Error { get; set; }
}
