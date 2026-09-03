using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MySubscriptionListEndpoint
{
    private readonly ILogger<MySubscriptionListEndpoint> _logger;

    public MySubscriptionListEndpoint(ILogger<MySubscriptionListEndpoint> logger)
    {
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", async (
                HttpContext httpContext,
                ISubscriptionBillingService billingService) =>
                await HandleAsync(httpContext, billingService))
            .RequireAuthorization()
            .Produces<System.Collections.Generic.IReadOnlyList<ShopperSubscriptionDto>>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        HttpContext httpContext,
        ISubscriptionBillingService billingService)
    {
        var username = httpContext.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            return Results.Unauthorized();
        }

        try
        {
            return Results.Ok(await billingService.ListForUserAsync(username, httpContext.RequestAborted));
        }
        catch (SubscriptionBillingException exception)
        {
            return SubscriptionEndpointResults.FromException(exception, httpContext, _logger);
        }
    }
}
