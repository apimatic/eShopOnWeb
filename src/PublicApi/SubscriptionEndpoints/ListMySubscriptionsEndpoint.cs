using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated user's Maxio subscriptions
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest, ISubscriptionService, UserManager<ApplicationUser>>
{
    private readonly ILogger<ListMySubscriptionsEndpoint> _logger;

    public ListMySubscriptionsEndpoint(ILogger<ListMySubscriptionsEndpoint> logger)
    {
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (HttpContext httpContext, ISubscriptionService subscriptionService, UserManager<ApplicationUser> userManager) =>
            {
                var request = new ListMySubscriptionsRequest
                {
                    Username = httpContext.User.Identity?.Name ?? string.Empty
                };
                return await HandleAsync(request, subscriptionService, userManager);
            })
            .RequireAuthorization()
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMySubscriptionsRequest request, ISubscriptionService subscriptionService, UserManager<ApplicationUser> userManager)
    {
        var response = new ListMySubscriptionsResponse(request.CorrelationId());

        var user = await userManager.FindByNameAsync(request.Username);
        if (user == null)
        {
            return Results.Unauthorized();
        }

        return await SubscriptionEndpointHelpers.ExecuteAsync(
            _logger,
            async () =>
            {
                var subscriptions = await subscriptionService.ListForUserAsync(user.Id);
                response.Subscriptions.AddRange(subscriptions.Select(CreateSubscriptionEndpoint.Map));
                return Results.Ok(response);
            });
    }
}
