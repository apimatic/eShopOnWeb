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

public class SubscribeRequest : BaseRequest
{
    /// <summary>The handle of the plan, as listed by GET api/plans.</summary>
    public string? PlanHandle { get; set; }
    public string? Handle { get; set; }
    public string? ProductHandle { get; set; }

    public string? ResolvedPlanHandle => PlanHandle ?? Handle ?? ProductHandle;
}

/// <summary>
/// Subscribes the signed-in shopper to a plan, without card details
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ClaimsPrincipal>
{
    private readonly SubscriptionService _subscriptions;

    public SubscribeEndpoint(SubscriptionService subscriptions)
    {
        _subscriptions = subscriptions;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (SubscribeRequest request, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, user);
            })
            .Produces<SubscriptionDto>(StatusCodes.Status201Created)
            .WithTags("BillingEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ClaimsPrincipal user)
    {
        var subscription = await _subscriptions.SubscribeAsync(BillingHelpers.UserName(user), request.ResolvedPlanHandle);
        return Results.Created($"api/subscriptions/{subscription.Id}", subscription);
    }
}
