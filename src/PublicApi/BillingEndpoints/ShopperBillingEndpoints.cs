using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

/// <summary>
/// Subscription features for signed-in shoppers. The caller is always taken from the JWT.
/// </summary>
public class ShopperBillingEndpoints : IEndpoint
{
    private const string TAG = "BillingEndpoints";
    private const string JWT = JwtBearerDefaults.AuthenticationScheme;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/plans",
            [Authorize(AuthenticationSchemes = JWT)] async (BillingService billing) =>
                Results.Ok(await billing.ListPlansAsync()))
            .Produces<PlanDto[]>().WithTags(TAG);

        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JWT)] async (ClaimsPrincipal user, SubscribeRequest request, BillingService billing) =>
            {
                var dto = await billing.SubscribeAsync(user.Identity?.Name, request?.PlanHandle);
                return Results.Created($"api/subscriptions/{dto.Id}", dto);
            })
            .Produces<SubscriptionDto>(StatusCodes.Status201Created).WithTags(TAG);

        app.MapGet("api/subscriptions/{subscriptionId:int}",
            [Authorize(AuthenticationSchemes = JWT)] async (ClaimsPrincipal user, int subscriptionId, BillingService billing) =>
                Results.Ok(await billing.GetSubscriptionAsync(user.Identity?.Name, subscriptionId)))
            .Produces<SubscriptionDto>().WithTags(TAG);

        app.MapPost("api/subscriptions/{subscriptionId:int}/pause",
            [Authorize(AuthenticationSchemes = JWT)] async (ClaimsPrincipal user, int subscriptionId, BillingService billing) =>
                Results.Ok(await billing.PauseAsync(user.Identity?.Name, subscriptionId)))
            .Produces<SubscriptionDto>().WithTags(TAG);

        app.MapPost("api/subscriptions/{subscriptionId:int}/resume",
            [Authorize(AuthenticationSchemes = JWT)] async (ClaimsPrincipal user, int subscriptionId, BillingService billing) =>
                Results.Ok(await billing.ResumeAsync(user.Identity?.Name, subscriptionId)))
            .Produces<SubscriptionDto>().WithTags(TAG);

        app.MapPost("api/subscriptions/{subscriptionId:int}/cancel",
            [Authorize(AuthenticationSchemes = JWT)] async (ClaimsPrincipal user, int subscriptionId, BillingService billing) =>
                Results.Ok(await billing.CancelAsync(user.Identity?.Name, subscriptionId)))
            .Produces<SubscriptionDto>().WithTags(TAG);

        app.MapPost("api/subscriptions/{subscriptionId:int}/restart",
            [Authorize(AuthenticationSchemes = JWT)] async (ClaimsPrincipal user, int subscriptionId, BillingService billing) =>
                Results.Ok(await billing.RestartAsync(user.Identity?.Name, subscriptionId)))
            .Produces<SubscriptionDto>().WithTags(TAG);

        app.MapPost("api/subscriptions/{subscriptionId:int}/switch",
            [Authorize(AuthenticationSchemes = JWT)] async (ClaimsPrincipal user, int subscriptionId, SwitchPlanRequest request, BillingService billing) =>
                Results.Ok(await billing.SwitchPlanAsync(user.Identity?.Name, subscriptionId, request?.PlanHandle)))
            .Produces<SubscriptionDto>().WithTags(TAG);

        app.MapGet("api/subscriptions/{subscriptionId:int}/usage",
            [Authorize(AuthenticationSchemes = JWT)] async (ClaimsPrincipal user, int subscriptionId, string? date, BillingService billing) =>
                Results.Ok(await billing.GetUsageAsync(user.Identity?.Name, subscriptionId, date)))
            .Produces<DailyUsageDto>().WithTags(TAG);
    }
}
