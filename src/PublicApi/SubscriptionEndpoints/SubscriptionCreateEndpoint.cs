using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Create a subscription for the authenticated user
/// </summary>
public class SubscriptionCreateEndpoint : IEndpoint<IResult>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", Handle)
            .Produces<SubscriptionCreateResponse>()
            .RequireAuthorization()
            .WithName("CreateSubscription")
            .WithTags("Subscriptions");
    }

    public async Task<IResult> Handle(SubscriptionCreateRequest request, HttpContext httpContext,
                                          UserManager<ApplicationUser> userManager, IMaxioService maxioService)
    {
        try
        {
            // Get the current user from the JWT token
            var userName = httpContext.User.FindFirst(ClaimTypes.Name)?.Value;
            if (string.IsNullOrEmpty(userName))
            {
                return Results.Unauthorized();
            }

            var user = await userManager.FindByNameAsync(userName);
            if (user == null)
            {
                return Results.NotFound("User not found");
            }

            // Get or create customer in Maxio
            var firstName = user.UserName ?? "User";
            var lastName = user.UserName ?? "";
            var customer = await maxioService.GetOrCreateCustomerAsync(
                user.Id,
                user.Email ?? "noemail@example.com",
                firstName,
                lastName);

            // Create subscription
            var subscription = await maxioService.CreateSubscriptionAsync(
                customer.Reference,
                request.ProductHandle);

            var response = new SubscriptionCreateResponse
            {
                Id = subscription.Id,
                State = subscription.State,
                ProductHandle = subscription.ProductHandle,
                ProductName = subscription.ProductName,
                NextBillingAt = subscription.NextAssessmentAt,
                CurrentPeriodStartsAt = subscription.CurrentPeriodStartsAt,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt
            };

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    public Task<IResult> HandleAsync()
    {
        throw new NotImplementedException();
    }
}

public class SubscriptionCreateRequest : BaseRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public class SubscriptionCreateResponse
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public DateTime? NextBillingAt { get; set; }
    public DateTime? CurrentPeriodStartsAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
}
