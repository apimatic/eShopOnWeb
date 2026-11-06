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

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SwitchPlanRequest : BaseRequest
{
    /// <summary>The handle of the plan to switch to.</summary>
    public string? PlanHandle { get; set; }
}

/// <summary>
/// Switches one of the caller's subscriptions (cancelled ones included) to another plan
/// </summary>
public class SwitchPlanEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/switch",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, SwitchPlanRequest request, ClaimsPrincipal user, IBillingService billing, CancellationToken ct) =>
            {
                return await HandleAsync(subscriptionId, request, user, billing, ct);
            })
            .Produces<SubscriptionDto>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(int subscriptionId, SwitchPlanRequest request, ClaimsPrincipal user, IBillingService billing, CancellationToken ct)
    {
        return Results.Ok(await billing.SwitchPlanAsync(user.Identity?.Name ?? string.Empty, subscriptionId, request.PlanHandle, ct));
    }
}
