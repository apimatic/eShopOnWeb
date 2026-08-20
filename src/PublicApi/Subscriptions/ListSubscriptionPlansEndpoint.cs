using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class ListSubscriptionPlansEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            (ISubscriptionBillingService billing,
                ILogger<ListSubscriptionPlansEndpoint> logger,
                CancellationToken cancellationToken) =>
                HandleAsync(billing, logger, cancellationToken))
            .Produces<SubscriptionPlanDto[]>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(
        ISubscriptionBillingService billing,
        ILogger<ListSubscriptionPlansEndpoint> logger,
        CancellationToken cancellationToken) =>
        SubscriptionEndpointSupport.ExecuteAsync(
            async () => Results.Ok(await billing.GetPlansAsync(cancellationToken)),
            logger);
}
