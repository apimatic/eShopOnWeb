using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionPlanListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionBillingService billingService, CancellationToken cancellationToken) =>
                await HandleAsync(billingService, cancellationToken))
            .Produces<SubscriptionPlan[]>(StatusCodes.Status200OK)
            .WithTags("Subscriptions");
    }

    public async System.Threading.Tasks.Task<IResult> HandleAsync(
        ISubscriptionBillingService billingService,
        CancellationToken cancellationToken)
    {
        var plans = await billingService.ListPlansAsync(cancellationToken);
        return Results.Ok(plans);
    }
}
