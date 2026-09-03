using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionPlanListEndpoint
{
    private readonly ILogger<SubscriptionPlanListEndpoint> _logger;

    public SubscriptionPlanListEndpoint(ILogger<SubscriptionPlanListEndpoint> logger)
    {
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", async (
                HttpContext httpContext,
                ISubscriptionBillingService billingService) =>
                await HandleAsync(httpContext, billingService))
            .RequireAuthorization()
            .Produces<System.Collections.Generic.IReadOnlyList<SubscriptionPlanDto>>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        HttpContext httpContext,
        ISubscriptionBillingService billingService)
    {
        try
        {
            return Results.Ok(await billingService.ListPlansAsync(httpContext.RequestAborted));
        }
        catch (SubscriptionBillingException exception)
        {
            return SubscriptionEndpointResults.FromException(exception, httpContext, _logger);
        }
    }
}
