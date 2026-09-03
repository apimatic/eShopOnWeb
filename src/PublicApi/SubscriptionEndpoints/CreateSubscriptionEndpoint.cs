using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class CreateSubscriptionEndpoint
{
    private readonly ILogger<CreateSubscriptionEndpoint> _logger;

    public CreateSubscriptionEndpoint(ILogger<CreateSubscriptionEndpoint> logger)
    {
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", async (
                CreateSubscriptionRequest request,
                HttpContext httpContext,
                ISubscriptionBillingService billingService) =>
                await HandleAsync(request, httpContext, billingService))
            .RequireAuthorization()
            .Produces<ShopperSubscriptionDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
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
            var subscription = await billingService.SubscribeAsync(
                username,
                request.ProductHandle,
                httpContext.RequestAborted);
            return Results.Ok(subscription);
        }
        catch (SubscriptionBillingException exception)
        {
            return SubscriptionEndpointResults.FromException(exception, httpContext, _logger);
        }
    }
}
