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

/// <summary>
/// Lists the signed-in shopper's billing subscriptions as visible in their account.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly UserManager<ApplicationUser> _userManager;

    public ListMySubscriptionsEndpoint(IHttpContextAccessor httpContextAccessor, UserManager<ApplicationUser> userManager)
    {
        _httpContextAccessor = httpContextAccessor;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionBillingService billingService) =>
            {
                return await HandleAsync(billingService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionBillingService billingService)
    {
        var response = new ListMySubscriptionsResponse();

        var user = await SubscriptionUserResolver.ResolveAsync(_httpContextAccessor.HttpContext?.User, _userManager);
        if (user is null || string.IsNullOrWhiteSpace(user.Email))
        {
            return Results.Unauthorized();
        }

        var summaries = await billingService.ListForUserAsync(user.Email, default);

        response.Subscriptions.AddRange(summaries.Select(summary => new SubscriptionDto
        {
            SubscriptionId = summary.SubscriptionId,
            PlanHandle = summary.PlanHandle,
            PlanName = summary.PlanName,
            State = summary.State,
            Price = summary.Price,
            PriceInCents = summary.PriceInCents,
            NextBillingDate = summary.NextBillingDate
        }));

        return Results.Ok(response);
    }
}