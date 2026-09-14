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
/// Lists the subscription plans on offer, read live from the billing system of record.
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, CancellationToken>
{
    private readonly ISubscriptionBillingService _subscriptionBilling;

    public ListSubscriptionPlansEndpoint(ISubscriptionBillingService subscriptionBilling)
    {
        _subscriptionBilling = subscriptionBilling;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CancellationToken cancellationToken) => await HandleAsync(cancellationToken))
            .Produces<ListSubscriptionPlansResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CancellationToken cancellationToken)
    {
        var plans = await _subscriptionBilling.ListPlansAsync(cancellationToken);

        var response = new ListSubscriptionPlansResponse
        {
            SubscriptionPlans = plans.Select(plan => plan.ToDto()).ToList(),
        };

        return Results.Ok(response);
    }
}
