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
/// Lists the authenticated user's subscriptions from Maxio Advanced Billing.
/// </summary>
public class MySubscriptionListEndpoint : IEndpoint<IResult>
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly UserManager<ApplicationUser> _userManager;

    public MySubscriptionListEndpoint(
        ISubscriptionService subscriptionService,
        IHttpContextAccessor httpContextAccessor,
        UserManager<ApplicationUser> userManager)
    {
        _subscriptionService = subscriptionService;
        _httpContextAccessor = httpContextAccessor;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async () =>
            {
                return await HandleAsync();
            })
            .Produces<MySubscriptionListResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        var user = await AuthenticatedUser.GetAsync(_httpContextAccessor, _userManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var subscriptions = await _subscriptionService.ListUserSubscriptionsAsync(user.Id);

        var response = new MySubscriptionListResponse
        {
            Subscriptions = subscriptions.Select(s => new SubscriptionDto
            {
                Id = s.Id,
                Reference = s.Reference,
                PlanHandle = s.PlanHandle,
                PlanName = s.PlanName,
                PriceInCents = s.PriceInCents,
                State = s.State,
                CustomerId = s.CustomerId,
                ActivatedAt = s.ActivatedAt,
                NextBillingAt = s.NextBillingAt,
                CanceledAt = s.CanceledAt,
                CancelAtEndOfPeriod = s.CancelAtEndOfPeriod
            }).ToList()
        };

        return Results.Ok(response);
    }
}
