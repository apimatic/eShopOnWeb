using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the recurring plans a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult, CancellationToken>
{
    private readonly ISubscriptionBillingService _subscriptionBillingService;

    public SubscriptionPlanListEndpoint(ISubscriptionBillingService subscriptionBillingService)
    {
        _subscriptionBillingService = subscriptionBillingService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CancellationToken cancellationToken) =>
            {
                return await HandleAsync(cancellationToken);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CancellationToken cancellationToken)
    {
        var response = new ListSubscriptionPlansResponse();

        try
        {
            var plans = await _subscriptionBillingService.GetPlansAsync(cancellationToken);
            response.SubscriptionPlans.AddRange(plans.Select(plan => plan.ToDto()));
        }
        catch (BillingException ex)
        {
            return BillingResults.Problem(ex, response.CorrelationId());
        }

        return Results.Ok(response);
    }
}
