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
/// Shows the shopper's metered usage on one day
/// </summary>
public class GetSubscriptionUsageEndpoint : IEndpoint<IResult, int, string?, ClaimsPrincipal>
{
    private readonly SubscriptionService _subscriptions;

    public GetSubscriptionUsageEndpoint(SubscriptionService subscriptions)
    {
        _subscriptions = subscriptions;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId}/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (int subscriptionId, string? date, ClaimsPrincipal user) =>
            {
                return await HandleAsync(subscriptionId, date, user);
            })
            .Produces<UsageDto>()
            .WithTags("BillingEndpoints");
    }

    public async Task<IResult> HandleAsync(int subscriptionId, string? date, ClaimsPrincipal user)
    {
        if (!BillingHelpers.TryParseDay(date, out var day))
        {
            throw new BillingException(400, "The date query parameter is required, as YYYY-MM-DD.");
        }

        return Results.Ok(await _subscriptions.GetUsageAsync(BillingHelpers.UserName(user), subscriptionId, day));
    }
}
