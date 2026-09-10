using System.Linq;
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
/// GET /api/subscription-plans — lists the plans a shopper can subscribe to (products in the configured
/// Maxio product family).
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, HttpContext, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, IMaxioBillingService billingService) =>
            {
                return await HandleAsync(httpContext, billingService);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext httpContext, IMaxioBillingService billingService)
    {
        var response = new ListSubscriptionPlansResponse();

        try
        {
            var plans = await billingService.GetPlansAsync(httpContext.RequestAborted);
            response.Plans = plans.Select(SubscriptionPlanDto.From).ToList();
        }
        catch (MaxioBillingException ex)
        {
            return SubscriptionEndpointHelpers.ToErrorResult(ex);
        }

        return Results.Ok(response);
    }
}
