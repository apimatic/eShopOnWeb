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

/// <summary>
/// Resumes a paused subscription
/// </summary>
public class ResumeSubscriptionEndpoint : IEndpoint<IResult, int, ClaimsPrincipal>
{
    private readonly SubscriptionService _subscriptions;

    public ResumeSubscriptionEndpoint(SubscriptionService subscriptions)
    {
        _subscriptions = subscriptions;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/resume",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (int subscriptionId, ClaimsPrincipal user) =>
            {
                return await HandleAsync(subscriptionId, user);
            })
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }

    public async Task<IResult> HandleAsync(int subscriptionId, ClaimsPrincipal user)
    {
        return Results.Ok(await _subscriptions.ResumeAsync(BillingHelpers.UserName(user), subscriptionId));
    }
}
