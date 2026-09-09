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
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated user's subscriptions with plan, price, state and
/// next-billing date.
/// </summary>
public class SubscriptionListMyEndpoint : IEndpoint<IResult, MySubscriptionsListRequest, IMaxioBillingService>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriptionListMyEndpoint(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal principal, IMaxioBillingService billingService) =>
            {
                var appUser = await _userManager.FindByNameAsync(principal.FindFirstValue(ClaimTypes.Name) ?? string.Empty);
                if (appUser is null)
                {
                    return Results.Unauthorized();
                }
                return await HandleAsync(new MySubscriptionsListRequest(), billingService, appUser);
            })
            .Produces<MySubscriptionsListResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MySubscriptionsListRequest request, IMaxioBillingService billingService)
    {
        return await HandleAsync(request, billingService, null);
    }

    private async Task<IResult> HandleAsync(MySubscriptionsListRequest request, IMaxioBillingService billingService,
        ApplicationUser? appUser)
    {
        if (appUser is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscriptions = await billingService.ListSubscriptionsForUserAsync(appUser.Id, CancellationToken.None);
            return Results.Ok(new MySubscriptionsListResponse(request.CorrelationId())
            {
                Subscriptions = subscriptions.ToList()
            });
        }
        catch (MaxioBillingException ex)
        {
            return Results.Json(new { message = ex.Message }, statusCode: ex.StatusCode);
        }
    }
}
