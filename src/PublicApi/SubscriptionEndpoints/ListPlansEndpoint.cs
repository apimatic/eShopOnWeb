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
/// Lists the subscription plans a customer can enrol in (UC1, step 1)
/// </summary>
public class ListPlansEndpoint : IEndpoint<IResult, ListPlansRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(new ListPlansRequest(), subscriptionService, cancellationToken);
            })
            .Produces<ListPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ListPlansRequest request, ISubscriptionService subscriptionService) =>
        HandleAsync(request, subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(ListPlansRequest request,
        ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        var response = new ListPlansResponse(request.CorrelationId());

        var plans = await subscriptionService.GetAvailablePlansAsync(cancellationToken);
        response.Plans = plans.Select(plan => plan.ToDto()).ToList();

        return Results.Ok(response);
    }
}
