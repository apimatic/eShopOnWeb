using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Switches one of the caller's subscriptions to another plan
/// </summary>
public class SwitchPlanEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/switch",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, SwitchPlanRequest request, ClaimsPrincipal user, CancellationToken ct, BillingService billing) =>
            {
                return Results.Ok(await billing.SwitchPlanAsync(Caller.Name(user), subscriptionId, request.PlanHandle, ct));
            })
            .Produces<SubscriptionDto>()
            .WithTags("SubscriptionEndpoints");
    }
}
