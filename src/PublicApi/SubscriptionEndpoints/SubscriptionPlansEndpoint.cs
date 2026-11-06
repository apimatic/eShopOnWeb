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
/// Lists the subscription plans on offer
/// </summary>
public class SubscriptionPlansEndpoint : IEndpoint<IResult, ISubscriptionService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionService subscriptions, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(subscriptions, cancellationToken);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionService subscriptions, CancellationToken cancellationToken)
    {
        var response = new ListSubscriptionPlansResponse();
        var plans = await subscriptions.ListPlansAsync(cancellationToken);
        response.Plans.AddRange(plans.Plans);
        response.IsTruncated = plans.IsTruncated;
        return Results.Ok(response);
    }
}
