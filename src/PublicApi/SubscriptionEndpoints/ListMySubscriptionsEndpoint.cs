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
/// Lists the authenticated user's subscriptions as recorded in Maxio Advanced Billing.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest, ISubscriptionService>
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
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(new ListMySubscriptionsRequest(), subscriptionService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMySubscriptionsRequest request, ISubscriptionService subscriptionService)
    {
        var response = new ListMySubscriptionsResponse(request.CorrelationId());

        var userName = _httpContextAccessor.HttpContext?.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        var applicationUser = await _userManager.FindByNameAsync(userName);
        if (applicationUser is null)
        {
            return Results.Unauthorized();
        }

        var subscriptions = await subscriptionService.GetMySubscriptionsAsync(applicationUser.Id);
        response.Subscriptions.AddRange(subscriptions.Select(s => new SubscriptionDto
        {
            SubscriptionId = s.SubscriptionId,
            Reference = s.Reference,
            PlanHandle = s.PlanHandle,
            PlanName = s.PlanName,
            Price = s.Price,
            State = s.State,
            NextBillingAt = s.NextBillingAt,
            CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
            ActivatedAt = s.ActivatedAt,
            CanceledAt = s.CanceledAt
        }));

        return Results.Ok(response);
    }
}