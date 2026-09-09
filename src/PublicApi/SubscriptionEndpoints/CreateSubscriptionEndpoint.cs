using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
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
/// Subscribes the authenticated user to a subscription plan. The caller's identity comes from the
/// JWT bearer token; a Maxio customer is ensured for the user and the subscription create is
/// idempotent, so a double-click never produces duplicates.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(UserManager<ApplicationUser> userManager, IHttpContextAccessor httpContextAccessor)
    {
        _userManager = userManager;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ISubscriptionBillingService billingService) =>
            {
                return await HandleAsync(request, billingService);
            })
           .Produces<CreateSubscriptionResponse>()
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billingService)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        var userName = httpContext?.User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(userName))
        {
            return Results.Unauthorized();
        }

        var applicationUser = await _userManager.FindByNameAsync(userName);
        if (applicationUser is null)
        {
            return Results.Unauthorized();
        }

        var subscription = await billingService.SubscribeAsync(
            applicationUser.Id,
            applicationUser.Email ?? userName,
            request.PlanHandle,
            httpContext?.RequestAborted ?? CancellationToken.None);

        var response = new CreateSubscriptionResponse
        {
            Subscription = new SubscriptionDto
            {
                SubscriptionId = subscription.SubscriptionId,
                PlanHandle = subscription.PlanHandle,
                PlanName = subscription.PlanName,
                Price = subscription.Price,
                State = subscription.State,
                NextBillingDate = subscription.NextBillingDate,
                Reference = subscription.Reference,
                CustomerId = subscription.CustomerId
            }
        };

        return Results.Created("api/my-subscriptions", response);
    }
}
