using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Shows the caller's metered usage on a single day (?date=YYYY-MM-DD)
/// </summary>
public class GetUsageEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId:int}/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, string? date, ClaimsPrincipal user, IBillingService billing, CancellationToken ct) =>
            {
                return await HandleAsync(subscriptionId, date, user, billing, ct);
            })
            .Produces<UsageDto>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(int subscriptionId, string? date, ClaimsPrincipal user, IBillingService billing, CancellationToken ct)
    {
        return Results.Ok(await billing.GetUsageAsync(user.Identity?.Name ?? string.Empty, subscriptionId, date, ct));
    }
}
