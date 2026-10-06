using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated shopper's Maxio subscriptions
/// </summary>
public class GetMySubscriptionsEndpoint : IEndpoint<IResult, GetMySubscriptionsRequest, IMaxioSubscriptionService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly UserManager<ApplicationUser> _userManager;

    public GetMySubscriptionsEndpoint(IHttpContextAccessor httpContextAccessor, UserManager<ApplicationUser> userManager)
    {
        _httpContextAccessor = httpContextAccessor;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioSubscriptionService subscriptionService) =>
            {
                return await HandleAsync(new GetMySubscriptionsRequest(), subscriptionService);
            })
            .Produces<GetMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(GetMySubscriptionsRequest request, IMaxioSubscriptionService subscriptionService)
    {
        var email = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(email))
            return Results.Unauthorized();
        var user = await _userManager.FindByEmailAsync(email);
        if (user is null)
            return Results.Unauthorized();

        var subscriptions = await subscriptionService.GetSubscriptionsForUserAsync(user.Id, _httpContextAccessor.HttpContext?.RequestAborted ?? System.Threading.CancellationToken.None);
        var response = new GetMySubscriptionsResponse(request.CorrelationId())
        {
            Subscriptions = subscriptions.Select(s => new SubscriptionDto
            {
                Id = s.Id,
                PlanHandle = s.PlanHandle,
                PlanName = s.PlanName,
                PriceInCents = s.PriceInCents,
                Currency = s.Currency,
                State = s.State,
                NextBillingDate = s.NextBillingDate,
                CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                CollectionMethod = s.CollectionMethod
            }).ToList()
        };
        return Results.Ok(response);
    }
}