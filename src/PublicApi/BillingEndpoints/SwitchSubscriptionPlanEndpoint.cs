using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

/// <summary>
/// Switches a subscription (cancelled ones included) to another plan
/// </summary>
public class SwitchSubscriptionPlanEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/switch",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (int subscriptionId, SwitchPlanRequest request, BillingService billing, ClaimsPrincipal user, CancellationToken ct) =>
                await HandleAsync(subscriptionId, request, billing, user, ct))
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }

    public async Task<IResult> HandleAsync(int subscriptionId, SwitchPlanRequest request, BillingService billing, ClaimsPrincipal user, CancellationToken ct)
    {
        return Results.Ok(await billing.SwitchAsync(BillingEndpointSupport.UserName(user), subscriptionId, request?.PlanHandle, ct));
    }
}

public class SwitchPlanRequest
{
    /// <summary>The handle of the plan to switch to.</summary>
    public string? PlanHandle { get; set; }
}
