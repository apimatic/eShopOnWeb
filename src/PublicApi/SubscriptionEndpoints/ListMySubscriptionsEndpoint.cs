using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated user's subscriptions as recorded in Maxio.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest, ISubscriptionBillingService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly UserManager<ApplicationUser> _userManager;

    public ListMySubscriptionsEndpoint(
        IHttpContextAccessor httpContextAccessor,
        UserManager<ApplicationUser> userManager)
    {
        _httpContextAccessor = httpContextAccessor;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (ISubscriptionBillingService billingService) =>
            {
                return await HandleAsync(new ListMySubscriptionsRequest(), billingService);
            })
            .RequireAuthorization()
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMySubscriptionsRequest request, ISubscriptionBillingService billingService)
    {
        var response = new ListMySubscriptionsResponse(request.CorrelationId());

        var user = await CurrentUserResolver.ResolveAsync(_httpContextAccessor, _userManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var subscriptions = await billingService.ListSubscriptionsForUserAsync(CurrentUserResolver.GetUserBillingReference(user));

        foreach (var subscription in subscriptions)
        {
            response.Subscriptions.Add(new SubscriptionDto
            {
                SubscriptionId = subscription.SubscriptionId,
                State = subscription.State,
                PlanHandle = subscription.PlanHandle,
                PlanName = subscription.PlanName,
                PriceInCents = subscription.PriceInCents,
                Price = subscription.Price.ToString("0.00"),
                NextBillingDate = subscription.NextBillingDate,
                ActivatedAt = subscription.ActivatedAt
            });
        }

        return Results.Ok(response);
    }
}
