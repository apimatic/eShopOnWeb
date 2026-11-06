using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SwitchPlanRequest
{
    /// <summary>The handle of the plan to switch to.</summary>
    public string? PlanHandle { get; set; }
    public string? Handle { get; set; }
}

/// <summary>
/// Switches a subscription (even a cancelled one) to another plan
/// </summary>
public class SwitchSubscriptionPlanEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/switch",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (int subscriptionId, SwitchPlanRequest request, ClaimsPrincipal user, MaxioBillingService billing) =>
                await HandleAsync(subscriptionId, request, user, billing))
            .Produces<SubscriptionDto>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(int subscriptionId, SwitchPlanRequest request, ClaimsPrincipal user, MaxioBillingService billing)
    {
        return Results.Ok(await billing.SwitchPlanAsync(user.ShopperId(), subscriptionId, request.PlanHandle ?? request.Handle));
    }
}
