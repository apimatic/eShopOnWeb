using System.Security.Claims;
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

/// <summary>
/// List the authenticated shopper's Maxio subscriptions.
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, UserManager<ApplicationUser>>
{
    private readonly ISubscriptionBillingService _billingService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionsEndpoint(
        ISubscriptionBillingService billingService,
        IHttpContextAccessor httpContextAccessor)
    {
        _billingService = billingService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (UserManager<ApplicationUser> userManager) =>
            {
                return await HandleAsync(userManager);
            })
            .Produces<ListMySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(UserManager<ApplicationUser> userManager)
    {
        var principal = _httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal();
        var (shopper, error) = await ShopperIdentityFactory.FromUserAsync(principal, userManager);
        if (error is not null || shopper is null)
        {
            return error ?? Results.Unauthorized();
        }

        var response = new ListMySubscriptionsResponse();
        var subscriptions = await _billingService.ListMySubscriptionsAsync(shopper);
        response.Subscriptions.AddRange(subscriptions.ConvertAll(item => item.ToDto()));
        return Results.Ok(response);
    }
}
