using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.MaxioBilling.Configuration;
using Microsoft.eShopWeb.MaxioBilling.Exceptions;
using Microsoft.eShopWeb.MaxioBilling.Interfaces;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List the subscription plans a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    private readonly MaxioBillingOptions _options;

    public SubscriptionPlanListEndpoint(IOptions<MaxioBillingOptions> options)
    {
        _options = options.Value;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionBillingService billingService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(billingService, cancellationToken);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ISubscriptionBillingService billingService) =>
        HandleAsync(billingService, CancellationToken.None);

    public async Task<IResult> HandleAsync(ISubscriptionBillingService billingService, CancellationToken cancellationToken)
    {
        var response = new ListSubscriptionPlansResponse();

        try
        {
            var plans = await billingService.GetPlansAsync(cancellationToken);

            response.Plans.AddRange(plans.Select(plan => plan.ToDto(_options.DefaultPlanHandle)));
            response.DefaultPlanHandle = _options.DefaultPlanHandle;

            return Results.Ok(response);
        }
        catch (BillingException exception)
        {
            return BillingResults.Problem(exception);
        }
    }
}
