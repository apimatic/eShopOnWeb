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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. Idempotent: repeating the call for a plan
/// the shopper is already subscribed to returns the existing subscription.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                CreateSubscriptionRequest request,
                ClaimsPrincipal claimsPrincipal,
                UserManager<ApplicationUser> userManager,
                ISubscriptionBillingService billingService) =>
            {
                var username = claimsPrincipal.Identity?.Name;
                if (string.IsNullOrEmpty(username))
                {
                    return Results.Unauthorized();
                }

                var user = await userManager.FindByNameAsync(username);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                request.Shopper = new ShopperInfo(user.Id, user.Email ?? username, username);
                return await HandleAsync(request, billingService);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billingService)
    {
        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.BadRequest("ProductHandle is required.");
        }

        var result = await billingService.SubscribeAsync(request.Shopper!, request.ProductHandle);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = result.Subscription,
            Created = result.Created
        };

        return result.Created
            ? Results.Created($"api/my-subscriptions", response)
            : Results.Ok(response);
    }
}
