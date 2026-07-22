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
/// List the subscription plans available to subscribe to (UC1, step 1).
/// </summary>
public class ListPlansEndpoint : IEndpoint<IResult, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        // Browsing the catalogue needs no identity, mirroring the anonymous catalog-item list.
        app.MapGet("api/subscription-plans",
            async (ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
                await ListAsync(subscriptionService, cancellationToken))
            .Produces<ListPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ISubscriptionService subscriptionService) =>
        ListAsync(subscriptionService, CancellationToken.None);

    private static Task<IResult> ListAsync(ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
        SubscriptionEndpointSupport.ExecuteAsync(async () =>
        {
            var response = new ListPlansResponse();
            var plans = await subscriptionService.ListPlansAsync(cancellationToken);

            response.Plans.AddRange(plans.Select(SubscriptionEndpointSupport.ToDto));

            return Results.Ok(response);
        });
}
