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
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscriptions in Maxio Advanced Billing that belong to the authenticated shopper.
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, UserManager<ApplicationUser>, IMaxioBillingService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (UserManager<ApplicationUser> userManager, IMaxioBillingService maxioBillingService) =>
            {
                return await HandleAsync(userManager, maxioBillingService);
            })
           .Produces<MySubscriptionsResponse>()
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        UserManager<ApplicationUser> userManager,
        IMaxioBillingService maxioBillingService)
    {
        var userName = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(userName))
        {
            return Results.Unauthorized();
        }

        var user = await userManager.FindByNameAsync(userName);
        if (user == null)
        {
            return Results.Unauthorized();
        }

        var response = new MySubscriptionsResponse();
        var subscriptions = await maxioBillingService.GetSubscriptionsForCustomerReferenceAsync($"eshopweb-{user.Id}");
        response.Subscriptions.AddRange(subscriptions.Select(CreateSubscriptionEndpoint.MapSummary));

        return Results.Ok(response);
    }
}
