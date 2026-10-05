using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List the authenticated user's (from the JWT) subscriptions as recorded by Maxio
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly UserManager<ApplicationUser> _userManager;

    public MySubscriptionsEndpoint(IHttpContextAccessor httpContextAccessor, UserManager<ApplicationUser> userManager)
    {
        _httpContextAccessor = httpContextAccessor;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(subscriptionService);
            })
            .RequireAuthorization()
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionService subscriptionService)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        var username = user?.Identity?.Name
            ?? user?.FindFirstValue(ClaimTypes.Name)
            ?? user?.FindFirstValue("name");

        if (string.IsNullOrWhiteSpace(username))
        {
            return Results.Unauthorized();
        }

        var applicationUser = await _userManager.FindByNameAsync(username);
        if (applicationUser is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscriptions = await subscriptionService.ListMySubscriptionsAsync(applicationUser.Id);
            var response = new MySubscriptionsResponse();
            response.Subscriptions.AddRange(subscriptions);
            return Results.Ok(response);
        }
        catch (MaxioApiException ex)
        {
            var statusCode = ex.StatusCode is >= 400 and < 500 ? ex.StatusCode : 502;
            return Results.Problem(title: "Maxio API error", detail: ex.Message, statusCode: statusCode);
        }
        catch (MaxioConfigurationException ex)
        {
            return Results.Problem(title: "Billing system is not configured", detail: ex.Message, statusCode: 500);
        }
    }
}