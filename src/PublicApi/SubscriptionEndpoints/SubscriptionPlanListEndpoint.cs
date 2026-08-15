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

/// <summary>
/// Lists the subscription plans a shopper can subscribe to (the products in the configured Maxio
/// product family). JWT-authenticated.
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioBillingService billing, CancellationToken ct) => await HandleAsync(billing, ct))
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    // Satisfies IEndpoint; the route lambda calls the cancellation-aware overload.
    public Task<IResult> HandleAsync(IMaxioBillingService billing) => HandleAsync(billing, CancellationToken.None);

    public async Task<IResult> HandleAsync(IMaxioBillingService billing, CancellationToken cancellationToken)
    {
        var plans = await billing.ListPlansAsync(cancellationToken);

        var response = new ListSubscriptionPlansResponse
        {
            Plans = plans.Select(p => p.ToDto()).ToList()
        };

        return Results.Ok(response);
    }
}
