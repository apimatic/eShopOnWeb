using System.Linq;
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
/// List the subscription plans on offer (the products of the configured Maxio product family)
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult, SubscriptionCaller, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscriptionCaller caller, ISubscriptionBillingService billing) =>
            {
                return await HandleAsync(caller, billing);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .Produces<SubscriptionErrorResponse>(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscriptionCaller caller, ISubscriptionBillingService billing)
    {
        var response = new ListSubscriptionPlansResponse();

        var result = await billing.ListPlansAsync(caller.RequestAborted);
        if (!result.IsSuccess)
        {
            return result.ToFailureResult(response.CorrelationId());
        }

        response.ProductFamilyHandle = result.Value!.ProductFamilyHandle;
        response.Plans.AddRange(result.Value.Plans.Select(SubscriptionPlanDto.From));
        response.IsTruncated = result.Value.IsTruncated;
        return Results.Ok(response);
    }
}
