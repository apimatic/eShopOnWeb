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

/// <summary>
/// Shows the metered usage of one of the signed-in shopper's subscriptions on a single day
/// </summary>
public class SubscriptionUsageEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId}/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, string? date, ClaimsPrincipal user, MaxioGateway billing) =>
            {
                return await HandleAsync(subscriptionId, date, user, billing);
            })
            .Produces<UsageDto>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(int subscriptionId, string? date, ClaimsPrincipal user, MaxioGateway billing)
    {
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var day))
        {
            throw new BillingException(400, "The date query parameter is required and must look like YYYY-MM-DD.");
        }

        return Results.Ok(await billing.UsageForDayAsync(user.Identity!.Name!, subscriptionId, day));
    }
}
