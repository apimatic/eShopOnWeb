using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans available in the configured Maxio product family.
/// </summary>
public class GetSubscriptionPlansEndpoint : IEndpoint<IResult, GetSubscriptionPlansRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionBillingService billingService, System.Threading.CancellationToken ct) =>
            {
                return await HandleAsync(new GetSubscriptionPlansRequest { CancellationToken = ct }, billingService);
            })
            .Produces<GetSubscriptionPlansResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(GetSubscriptionPlansRequest request, ISubscriptionBillingService billingService)
    {
        var response = new GetSubscriptionPlansResponse(request.CorrelationId());
        try
        {
            var plans = await billingService.GetPlansAsync(request.CancellationToken);
            response.Plans = plans.Select(p => p.ToDto()).ToList();
            return Results.Ok(response);
        }
        catch (MaxioBillingException ex)
        {
            return BillingProblemResults.ToResult(ex);
        }
    }
}
