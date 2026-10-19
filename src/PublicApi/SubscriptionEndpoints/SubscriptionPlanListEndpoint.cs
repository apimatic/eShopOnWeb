using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List the recurring plans a shopper can subscribe to
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionBillingService billingService, HttpContext httpContext) =>
            {
                return await HandleAsync(billingService, httpContext.RequestAborted);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .Produces<SubscriptionErrorResponse>(StatusCodes.Status502BadGateway)
            .WithTags(SubscriptionEndpointMappings.TAG);
    }

    public Task<IResult> HandleAsync(ISubscriptionBillingService billingService) =>
        HandleAsync(billingService, CancellationToken.None);

    public async Task<IResult> HandleAsync(ISubscriptionBillingService billingService, CancellationToken cancellationToken)
    {
        var response = new ListSubscriptionPlansResponse();
        try
        {
            var catalog = await billingService.GetPlansAsync(cancellationToken);
            response.Plans.AddRange(catalog.Plans.Select(SubscriptionEndpointMappings.ToDto));
            response.IsComplete = catalog.IsComplete;
            return Results.Ok(response);
        }
        catch (SubscriptionBillingException ex)
        {
            return SubscriptionEndpointMappings.ToResult(ex, response.CorrelationId());
        }
    }
}
