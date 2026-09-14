using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated user's subscriptions from the billing system of record.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ClaimsPrincipal, ISubscriptionBillingService>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public ListMySubscriptionsEndpoint(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal principal, ISubscriptionBillingService billingService) =>
            {
                return await HandleAsync(principal, billingService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal principal, ISubscriptionBillingService billingService)
    {
        var response = new ListMySubscriptionsResponse(Guid.NewGuid());

        var username = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            return Results.Unauthorized();
        }
        var user = await _userManager.FindByNameAsync(username);
        if (user == null)
        {
            return Results.Unauthorized();
        }

        var subscriptions = await billingService.GetSubscriptionsForUserAsync(new BillingUserInfo
        {
            UserId = user.Id,
            UserName = user.UserName ?? username,
            Email = user.Email ?? username
        });

        response.Subscriptions.AddRange(subscriptions.Select(s => new SubscriptionDto
        {
            SubscriptionId = s.SubscriptionId,
            PlanHandle = s.PlanHandle,
            PlanName = s.PlanName,
            Price = s.Price,
            State = s.State,
            NextBillingDate = s.NextBillingDate,
            ActivatedAt = s.ActivatedAt,
            CanceledAt = s.CanceledAt,
            CreatedAt = s.CreatedAt,
            IsActive = s.IsActive
        }));

        return Results.Ok(response);
    }
}
