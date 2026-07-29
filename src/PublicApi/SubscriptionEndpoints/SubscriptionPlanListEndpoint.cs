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
/// Lists the subscription plans available to subscribe to (the products in the
/// configured Maxio product family). Requires a valid JWT.
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioBillingService billingService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(billingService, cancellationToken);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(IMaxioBillingService billingService)
        => HandleAsync(billingService, CancellationToken.None);

    public async Task<IResult> HandleAsync(IMaxioBillingService billingService, CancellationToken cancellationToken)
    {
        var response = new ListSubscriptionPlansResponse();
        try
        {
            var plans = await billingService.GetPlansAsync(cancellationToken);
            response.Plans = plans.Select(p => p.ToDto()).ToList();
            return Results.Ok(response);
        }
        catch (MaxioBillingException ex)
        {
            return SubscriptionEndpointResults.FromBillingException(ex);
        }
    }
}
