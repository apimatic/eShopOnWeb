using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Lists currently available Maxio subscription plans.</summary>
public sealed class SubscriptionPlansEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IMaxioClient maxio, CancellationToken cancellationToken) =>
            {
                var plans = await maxio.ListPlansAsync(cancellationToken);
                return Results.Ok(plans.Where(plan => !plan.IsArchived).Select(plan => new SubscriptionPlanResponse(
                    plan.Handle, plan.Name, plan.PriceInCents, plan.Interval, plan.IntervalUnit)));
            })
            .Produces<SubscriptionPlanResponse[]>()
            .WithTags("SubscriptionEndpoints");
    }
}

public sealed record SubscriptionPlanResponse(string Handle, string Name, int PriceInCents, int Interval, string IntervalUnit);
