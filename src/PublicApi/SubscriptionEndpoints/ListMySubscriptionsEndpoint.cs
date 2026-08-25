using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated shopper's subscriptions (JWT required).
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, IMaxioBillingService>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListMySubscriptionsEndpoint(UserManager<ApplicationUser> userManager, IHttpContextAccessor httpContextAccessor)
    {
        _userManager = userManager;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (IMaxioBillingService billingService) =>
            {
                return await HandleAsync(billingService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization(policy => policy
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser());
    }

    public async Task<IResult> HandleAsync(IMaxioBillingService billingService)
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        var user = principal is null
            ? null
            : await SubscriptionEndpointHelpers.GetCurrentUserAsync(principal, _userManager);
        if (user is null)
            return Results.Unauthorized();

        var response = new ListMySubscriptionsResponse();

        try
        {
            var subscriptions = await billingService.GetSubscriptionsAsync(user.Id, user.UserName!);
            response.Subscriptions.AddRange(subscriptions.Select(SubscriptionDto.FromModel));
            return Results.Ok(response);
        }
        catch (MaxioApiException ex)
        {
            return SubscriptionEndpointHelpers.ToErrorResult(ex);
        }
    }
}
