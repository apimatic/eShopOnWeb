using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

public class SwitchPlanRequest
{
    /// <summary>The handle of the plan to switch to.</summary>
    public string? PlanHandle { get; set; }
}

/// <summary>
/// Switches one of the caller's subscriptions to another plan
/// </summary>
public class SwitchSubscriptionPlanEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/switch",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, SwitchPlanRequest request, ClaimsPrincipal user, IBillingService billing, CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(request?.PlanHandle))
                    throw BillingException.BadRequest("planHandle is required.");
                return Results.Ok(await billing.SwitchPlanAsync(BillingEndpointHelpers.CallerName(user), subscriptionId, request.PlanHandle.Trim(), ct));
            })
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }
}
