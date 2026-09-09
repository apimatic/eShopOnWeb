using System;
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
/// Cancels one of the authenticated user's subscriptions
/// </summary>
public class CancelSubscriptionEndpoint : IEndpoint<IResult, CancelSubscriptionRequest, ISubscriptionService, UserManager<ApplicationUser>>
{
    private readonly ILogger<CancelSubscriptionEndpoint> _logger;

    public CancelSubscriptionEndpoint(ILogger<CancelSubscriptionEndpoint> logger)
    {
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/subscriptions/{id}",
            async (int id, HttpContext httpContext, ISubscriptionService subscriptionService, UserManager<ApplicationUser> userManager) =>
            {
                var request = new CancelSubscriptionRequest
                {
                    SubscriptionId = id,
                    Username = httpContext.User.Identity?.Name ?? string.Empty
                };
                return await HandleAsync(request, subscriptionService, userManager);
            })
            .RequireAuthorization()
            .Produces<CancelSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelSubscriptionRequest request, ISubscriptionService subscriptionService, UserManager<ApplicationUser> userManager)
    {
        var response = new CancelSubscriptionResponse(request.CorrelationId());

        var user = await userManager.FindByNameAsync(request.Username);
        if (user == null)
        {
            return Results.Unauthorized();
        }

        return await SubscriptionEndpointHelpers.ExecuteAsync(
            _logger,
            async () =>
            {
                var details = await subscriptionService.CancelAsync(user.Id, request.SubscriptionId);
                response.Subscription = CreateSubscriptionEndpoint.Map(details);
                return Results.Ok(response);
            });
    }
}
