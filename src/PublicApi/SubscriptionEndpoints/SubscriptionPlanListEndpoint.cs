using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionPlanListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (ISubscriptionBillingService billing, CancellationToken cancellationToken) =>
                    await HandleAsync(billing, cancellationToken))
            .Produces<SubscriptionPlanListResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        ISubscriptionBillingService billing,
        CancellationToken cancellationToken)
    {
        var plans = await billing.GetPlansAsync(cancellationToken);
        return Results.Ok(new SubscriptionPlanListResponse
        {
            Plans = plans.Select(plan => plan.ToDto()).ToArray()
        });
    }
}
