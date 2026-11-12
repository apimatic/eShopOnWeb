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

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

public record SwitchPlanRequest(string? PlanHandle);

/// <summary>
/// Switches one of the signed-in shopper's subscriptions to another plan
/// </summary>
public class SwitchSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/switch",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, SwitchPlanRequest request, ClaimsPrincipal user, MaxioGateway billing) =>
            {
                return await HandleAsync(subscriptionId, request, user, billing);
            })
            .Produces<SubscriptionDto>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(int subscriptionId, SwitchPlanRequest request, ClaimsPrincipal user, MaxioGateway billing)
    {
        return Results.Ok(await billing.SwitchAsync(user.Identity!.Name!, subscriptionId, request.PlanHandle));
    }
}
