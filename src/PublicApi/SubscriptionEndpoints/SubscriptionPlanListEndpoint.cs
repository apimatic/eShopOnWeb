using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans a shopper can subscribe to.
/// </summary>
/// <remarks>
/// Plans are read from the billing system of record on every request (behind a short cache), so a
/// plan added or repriced there shows up here without redeploying eShopOnWeb.
/// </remarks>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionBillingService billingService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(billingService, cancellationToken);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    /// <summary>Overload required by <see cref="IEndpoint{TResponse, TService}"/>; the route above uses the
    /// cancellable one.</summary>
    public Task<IResult> HandleAsync(ISubscriptionBillingService billingService) =>
        HandleAsync(billingService, CancellationToken.None);

    public async Task<IResult> HandleAsync(ISubscriptionBillingService billingService, CancellationToken cancellationToken)
    {
        var response = new ListSubscriptionPlansResponse();

        var plans = await billingService.ListPlansAsync(cancellationToken);
        response.Plans.AddRange(plans.Select(p => p.ToDto()));

        return Results.Ok(response);
    }
}
