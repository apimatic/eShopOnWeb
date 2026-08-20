using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribe the authenticated shopper to a Maxio plan.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateShopperSubscriptionRequest, ISubscriptionBillingService>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(
        UserManager<ApplicationUser> userManager,
        IHttpContextAccessor httpContextAccessor)
    {
        _userManager = userManager;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateShopperSubscriptionRequest request, ISubscriptionBillingService billing) =>
            {
                return await HandleAsync(request, billing);
            })
            .Produces<CreateShopperSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        CreateShopperSubscriptionRequest request,
        ISubscriptionBillingService billing)
    {
        var shopper = await ShopperIdentity.ResolveAsync(_httpContextAccessor.HttpContext, _userManager);
        var subscription = await billing.SubscribeAsync(
            shopper.BuyerId,
            shopper.Email,
            shopper.DisplayName,
            request.ProductHandle);

        var response = new CreateShopperSubscriptionResponse
        {
            Subscription = SubscriptionDto.From(subscription)
        };

        return Results.Created($"api/subscriptions/{subscription.Id}", response);
    }
}

internal static class ShopperIdentity
{
    public static async Task<(string BuyerId, string Email, string? DisplayName)> ResolveAsync(
        HttpContext? httpContext,
        UserManager<ApplicationUser> users)
    {
        var userName = httpContext?.User?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new BillingException(401, "Authentication is required.");
        }

        var user = await users.FindByNameAsync(userName);
        if (user is null)
        {
            throw new BillingException(401, "The signed-in user could not be found.");
        }

        return (user.Id, user.Email ?? userName, user.UserName);
    }
}
