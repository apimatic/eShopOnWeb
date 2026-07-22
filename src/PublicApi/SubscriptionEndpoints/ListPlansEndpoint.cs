using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List the subscription plans on offer (UC1, step 1). Anonymous, like the catalog listings.
/// </summary>
public class ListPlansEndpoint : IEndpoint<IResult, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(subscriptionService, cancellationToken);
            })
            .Produces<ListPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ISubscriptionService subscriptionService)
        => HandleAsync(subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        var response = new ListPlansResponse();

        var plans = await subscriptionService.ListPlansAsync(cancellationToken);
        response.Plans.AddRange(plans.Select(plan => plan.ToDto()));

        return Results.Ok(response);
    }
}
