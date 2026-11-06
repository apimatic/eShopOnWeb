using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

/// <summary>
/// Shows the metered usage of one of the signed-in shoppers subscriptions on a single day
/// </summary>
public class GetSubscriptionUsageEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId}/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (int subscriptionId, string? date, BillingService billing, ClaimsPrincipal user, CancellationToken ct) =>
                await HandleAsync(subscriptionId, date, billing, user, ct))
            .Produces<SubscriptionUsageDto>()
            .WithTags("BillingEndpoints");
    }

    public async Task<IResult> HandleAsync(int subscriptionId, string? date, BillingService billing, ClaimsPrincipal user, CancellationToken ct)
    {
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var day))
        {
            throw BillingException.BadRequest("The date query parameter is required, formatted as YYYY-MM-DD.");
        }
        return Results.Ok(await billing.UsageAsync(BillingEndpointSupport.UserName(user), subscriptionId, day, ct));
    }
}
