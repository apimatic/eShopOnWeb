using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class ListMySubscriptionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            (HttpContext httpContext,
                UserManager<ApplicationUser> userManager,
                ISubscriptionBillingService billing,
                ILogger<ListMySubscriptionsEndpoint> logger,
                CancellationToken cancellationToken) =>
                HandleAsync(httpContext, userManager, billing, logger, cancellationToken))
            .Produces<SubscriptionDto[]>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        HttpContext httpContext,
        UserManager<ApplicationUser> userManager,
        ISubscriptionBillingService billing,
        ILogger<ListMySubscriptionsEndpoint> logger,
        CancellationToken cancellationToken)
    {
        var user = await SubscriptionEndpointSupport.FindUserAsync(httpContext.User, userManager);
        if (user == null)
        {
            return Results.Unauthorized();
        }

        return await SubscriptionEndpointSupport.ExecuteAsync(
            async () => Results.Ok(await billing.GetSubscriptionsAsync(user, cancellationToken)),
            logger);
    }
}
