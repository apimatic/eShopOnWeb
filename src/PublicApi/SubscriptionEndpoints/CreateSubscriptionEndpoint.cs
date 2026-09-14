using System;
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
/// Subscribes the authenticated user to a plan. Idempotent: enrolling twice never
/// creates two billing customers or two subscriptions.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal, ISubscriptionBillingService>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal principal, ISubscriptionBillingService billingService) =>
            {
                return await HandleAsync(request, principal, billingService);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal principal,
        ISubscriptionBillingService billingService)
    {
        var response = new CreateSubscriptionResponse(Guid.NewGuid());

        var userInfo = await ResolveUserAsync(principal);
        if (userInfo == null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscription = await billingService.SubscribeAsync(userInfo, request.PlanHandle);
            response.Subscription = new SubscriptionDto
            {
                SubscriptionId = subscription.SubscriptionId,
                PlanHandle = subscription.PlanHandle,
                PlanName = subscription.PlanName,
                Price = subscription.Price,
                State = subscription.State,
                NextBillingDate = subscription.NextBillingDate,
                ActivatedAt = subscription.ActivatedAt,
                CanceledAt = subscription.CanceledAt,
                CreatedAt = subscription.CreatedAt,
                IsActive = subscription.IsActive
            };
            return Results.Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    private async Task<BillingUserInfo?> ResolveUserAsync(ClaimsPrincipal principal)
    {
        var username = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            return null;
        }
        var user = await _userManager.FindByNameAsync(username);
        if (user == null)
        {
            return null;
        }
        return new BillingUserInfo
        {
            UserId = user.Id,
            UserName = user.UserName ?? username,
            Email = user.Email ?? username
        };
    }
}
