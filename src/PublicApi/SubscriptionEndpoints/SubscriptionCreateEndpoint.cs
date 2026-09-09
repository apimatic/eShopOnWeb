using System;
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
/// Subscribes the authenticated user to a plan. Idempotent: subscribing twice
/// to the same plan returns the existing subscription.
/// </summary>
public class SubscriptionCreateEndpoint : IEndpoint<IResult, SubscriptionCreateRequest, IMaxioBillingService>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriptionCreateEndpoint(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscriptionCreateRequest request, ClaimsPrincipal principal, IMaxioBillingService billingService) =>
            {
                var appUser = await _userManager.FindByNameAsync(principal.FindFirstValue(ClaimTypes.Name) ?? string.Empty);
                if (appUser is null || string.IsNullOrEmpty(appUser.Email))
                {
                    return Results.Unauthorized();
                }
                return await HandleAsync(request, billingService, appUser);
            })
            .Produces<SubscriptionCreateResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscriptionCreateRequest request, IMaxioBillingService billingService)
    {
        return await HandleAsync(request, billingService, null);
    }

    private async Task<IResult> HandleAsync(SubscriptionCreateRequest request, IMaxioBillingService billingService,
        ApplicationUser? appUser)
    {
        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.BadRequest(new { message = "productHandle is required." });
        }
        if (appUser is null || string.IsNullOrEmpty(appUser.Email))
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscription = await billingService.SubscribeAsync(appUser.Id, appUser.Email,
                request.ProductHandle, CancellationToken.None);
            return Results.Created($"api/my-subscriptions", new SubscriptionCreateResponse(request.CorrelationId())
            {
                Subscription = subscription
            });
        }
        catch (MaxioBillingException ex)
        {
            return Results.Json(new { message = ex.Message }, statusCode: ex.StatusCode);
        }
    }
}
