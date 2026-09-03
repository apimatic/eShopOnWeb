using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans available in the configured Maxio product family.
/// GET /api/subscription-plans (JWT-authenticated).
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, CancellationToken>
{
    private readonly ISubscriptionBillingService _billingService;

    public ListSubscriptionPlansEndpoint(ISubscriptionBillingService billingService)
    {
        _billingService = billingService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CancellationToken cancellationToken) => await HandleAsync(cancellationToken))
            .Produces<ListSubscriptionPlansResponse>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CancellationToken cancellationToken)
    {
        try
        {
            var plans = await _billingService.GetAvailablePlansAsync(cancellationToken);
            var response = new ListSubscriptionPlansResponse
            {
                Plans = plans.Select(p => p.ToDto()).ToList()
            };
            return Results.Ok(response);
        }
        catch (MaxioBillingException ex)
        {
            return ex.ToResult();
        }
    }
}
