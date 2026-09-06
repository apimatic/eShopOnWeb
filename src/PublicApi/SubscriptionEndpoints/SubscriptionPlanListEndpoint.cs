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
/// List the subscription plans a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanListEndpoint
    : IEndpoint<IResult, ListSubscriptionPlansRequest, ISubscriptionBillingService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (bool? includeComponents, ISubscriptionBillingService billingService, HttpContext httpContext) =>
            {
                return await HandleAsync(
                    new ListSubscriptionPlansRequest(includeComponents ?? false),
                    billingService,
                    httpContext);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        ListSubscriptionPlansRequest request,
        ISubscriptionBillingService billingService,
        HttpContext httpContext)
    {
        var response = new ListSubscriptionPlansResponse(request.CorrelationId());
        var cancellationToken = httpContext.RequestAborted;

        try
        {
            var plans = await billingService.GetPlansAsync(cancellationToken);
            response.Plans.AddRange(plans.Select(SubscriptionMapping.ToDto));

            if (request.IncludeComponents)
            {
                var components = await billingService.GetPlanComponentsAsync(cancellationToken);
                response.Components.AddRange(components.Select(SubscriptionMapping.ToDto));
            }

            return Results.Ok(response);
        }
        catch (BillingException ex)
        {
            return SubscriptionEndpointHelpers.ToProblem(ex);
        }
    }
}
