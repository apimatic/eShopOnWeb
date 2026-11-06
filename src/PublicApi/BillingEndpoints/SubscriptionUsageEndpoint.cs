using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

/// <summary>
/// Shows the metered usage of one of the signed-in shopper's subscriptions on a single day
/// </summary>
public class SubscriptionUsageEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId}/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, string? date, System.Security.Claims.ClaimsPrincipal user, MaxioBillingService billing) =>
            {
                return await HandleAsync(subscriptionId, date, BillingEndpointHelpers.UserName(user), billing);
            })
            .Produces<UsageDto>()
            .WithTags(BillingEndpointHelpers.TAG);
    }

    public async Task<IResult> HandleAsync(int subscriptionId, string? date, string userName, MaxioBillingService billing)
    {
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            throw BillingException.BadRequest("Query parameter 'date' is required and must look like YYYY-MM-DD.");
        return Results.Ok(await billing.GetUsageAsync(userName, subscriptionId, day));
    }
}
