using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

public class SwitchPlanRequest : BaseRequest
{
    /// <summary>Handle of the plan to switch to.</summary>
    public string? PlanHandle { get; set; }
    /// <summary>Alias of <see cref="PlanHandle"/>.</summary>
    public string? Handle { get; set; }

    internal string? ResolvedHandle => string.IsNullOrWhiteSpace(PlanHandle) ? Handle : PlanHandle;
}

/// <summary>
/// Switches one of the signed-in shopper's subscriptions (cancelled ones included) to another plan
/// </summary>
public class SubscriptionSwitchEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/switch",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, SwitchPlanRequest request, System.Security.Claims.ClaimsPrincipal user, MaxioBillingService billing) =>
            {
                return await HandleAsync(subscriptionId, request, BillingEndpointHelpers.UserName(user), billing);
            })
            .Produces<SubscriptionDto>()
            .WithTags(BillingEndpointHelpers.TAG);
    }

    public async Task<IResult> HandleAsync(int subscriptionId, SwitchPlanRequest request, string userName, MaxioBillingService billing)
    {
        return Results.Ok(await billing.SwitchPlanAsync(userName, subscriptionId, request.ResolvedHandle));
    }
}
