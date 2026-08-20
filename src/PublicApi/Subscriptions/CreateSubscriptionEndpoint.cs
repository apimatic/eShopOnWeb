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

public sealed class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            (CreateSubscriptionRequest request,
                HttpContext httpContext,
                UserManager<ApplicationUser> userManager,
                ISubscriptionBillingService billing,
                ILogger<CreateSubscriptionEndpoint> logger,
                CancellationToken cancellationToken) =>
                HandleAsync(request, httpContext, userManager, billing, logger, cancellationToken))
            .Produces<SubscriptionDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        HttpContext httpContext,
        UserManager<ApplicationUser> userManager,
        ISubscriptionBillingService billing,
        ILogger<CreateSubscriptionEndpoint> logger,
        CancellationToken cancellationToken)
    {
        var user = await SubscriptionEndpointSupport.FindUserAsync(httpContext.User, userManager);
        if (user == null)
        {
            return Results.Unauthorized();
        }

        return await SubscriptionEndpointSupport.ExecuteAsync(
            async () => Results.Ok(await billing.SubscribeAsync(
                user,
                request.ProductHandle,
                cancellationToken)),
            logger);
    }
}
