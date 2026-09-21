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
/// Lists the subscription plans a shopper can subscribe to (the non-archived products in the configured
/// Maxio product family). JWT-authenticated.
/// </summary>
public class SubscriptionPlansListEndpoint : IEndpoint<IResult, ISubscriptionBillingService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionBillingService billing, CancellationToken cancellationToken) =>
                await HandleAsync(billing, cancellationToken))
            .Produces<SubscriptionPlansResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionBillingService billing, CancellationToken cancellationToken)
    {
        try
        {
            var plans = await billing.GetPlansAsync(cancellationToken);
            var response = new SubscriptionPlansResponse();
            response.Plans.AddRange(plans.Select(SubscriptionMappings.ToDto));
            return Results.Ok(response);
        }
        catch (MaxioBillingException ex)
        {
            return SubscriptionMappings.ToProblemResult(ex);
        }
    }
}
