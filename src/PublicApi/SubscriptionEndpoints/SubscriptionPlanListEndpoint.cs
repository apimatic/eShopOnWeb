using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans available from Maxio Advanced Billing.
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioSubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(subscriptionService, cancellationToken);
            })
           .Produces<SubscriptionPlanListResponse>()
           .RequireAuthorization()
           .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(IMaxioSubscriptionService subscriptionService)
    {
        return HandleAsync(subscriptionService, CancellationToken.None);
    }

    public async Task<IResult> HandleAsync(IMaxioSubscriptionService subscriptionService, CancellationToken cancellationToken)
    {
        try
        {
            var plans = await subscriptionService.GetPlansAsync(cancellationToken);
            return Results.Ok(new SubscriptionPlanListResponse { Plans = plans.ToList() });
        }
        catch (MaxioBillingException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: ex.RecommendedHttpStatusCode);
        }
    }
}
