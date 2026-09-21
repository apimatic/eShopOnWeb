using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// GET /api/subscription-plans — lists the plans a shopper can subscribe to (the products in the
/// configured Maxio product family). JWT-authenticated.
/// </summary>
public class SubscriptionPlansEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionBillingService billing, CancellationToken ct) =>
            {
                try
                {
                    var plans = await billing.GetPlansAsync(ct);
                    return Results.Ok(new SubscriptionPlansResponse
                    {
                        Plans = plans.Select(SubscriptionEndpointHelpers.ToDto).ToList()
                    });
                }
                catch (SubscriptionBillingException ex)
                {
                    return SubscriptionEndpointHelpers.ToProblem(ex);
                }
            })
            .Produces<SubscriptionPlansResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags(SubscriptionEndpointHelpers.Tag);
    }
}
