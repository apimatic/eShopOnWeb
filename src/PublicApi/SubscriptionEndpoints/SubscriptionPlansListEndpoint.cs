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
/// Lists the subscribable plans (products in the configured Maxio product family).
/// </summary>
public class SubscriptionPlansListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                ISubscriptionBillingService billing,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var plans = await billing.GetPlansAsync(cancellationToken);
                    var response = new SubscriptionPlansResponse();
                    response.Plans.AddRange(plans);
                    return Results.Ok(response);
                }
                catch (SubscriptionBillingException ex)
                {
                    return SubscriptionEndpointHelpers.ToProblem(ex);
                }
            })
            .Produces<SubscriptionPlansResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }
}
