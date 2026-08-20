using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Enrolls the authenticated shopper in a Maxio subscription plan.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request,
                   ISubscriptionBillingService billing,
                   UserManager<ApplicationUser> userManager,
                   HttpContext httpContext) =>
            {
                return await HandleAsync(request, billing, userManager, httpContext.User);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billing) =>
        HandleAsync(request, billing, null, null);

    private static async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        ISubscriptionBillingService billing,
        UserManager<ApplicationUser>? userManager,
        ClaimsPrincipal? principal)
    {
        var user = await ResolveUserAsync(userManager, principal);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var subscription = await billing.SubscribeAsync(
            user.Id,
            user.Email ?? user.UserName ?? string.Empty,
            user.UserName ?? user.Email ?? string.Empty,
            request.ProductHandle);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = Map(subscription)
        };

        return subscription.Created
            ? Results.Created($"api/subscriptions/{subscription.Id}", response)
            : Results.Ok(response);
    }

    private static async Task<ApplicationUser?> ResolveUserAsync(
        UserManager<ApplicationUser>? userManager,
        ClaimsPrincipal? principal)
    {
        var userName = principal?.Identity?.Name;
        if (userManager is null || string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        return await userManager.FindByNameAsync(userName);
    }

    internal static SubscriptionDto Map(CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        ProductHandle = subscription.ProductHandle,
        ProductName = subscription.ProductName,
        Price = subscription.Price,
        State = subscription.State,
        NextBillingDate = subscription.NextBillingDate
    };
}
