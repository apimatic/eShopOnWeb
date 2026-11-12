using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

public class SwitchSubscriptionPlanRequest : BaseRequest
{
    /// <summary>The handle of the new plan, as listed by GET api/plans.</summary>
    public string? PlanHandle { get; set; }
    public string? Handle { get; set; }
    public string? ProductHandle { get; set; }

    public string? ResolvedPlanHandle => PlanHandle ?? Handle ?? ProductHandle;
}

/// <summary>
/// Switches one of the shopper's subscriptions to another plan, canceled or not
/// </summary>
public class SwitchSubscriptionPlanEndpoint : IEndpoint<IResult, int, SwitchSubscriptionPlanRequest, ClaimsPrincipal>
{
    private readonly SubscriptionService _subscriptions;

    public SwitchSubscriptionPlanEndpoint(SubscriptionService subscriptions)
    {
        _subscriptions = subscriptions;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/switch",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (int subscriptionId, SwitchSubscriptionPlanRequest request, ClaimsPrincipal user) =>
            {
                return await HandleAsync(subscriptionId, request, user);
            })
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }

    public async Task<IResult> HandleAsync(int subscriptionId, SwitchSubscriptionPlanRequest request, ClaimsPrincipal user)
    {
        return Results.Ok(await _subscriptions.SwitchPlanAsync(BillingHelpers.UserName(user), subscriptionId, request.ResolvedPlanHandle));
    }
}
