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

public sealed class SubscriptionPlanListEndpoint : IEndpoint<IResult, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ISubscriptionService service, CancellationToken cancellationToken) =>
                await HandleWithCancellationAsync(service, cancellationToken))
            .Produces<SubscriptionPlansResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ISubscriptionService service) => HandleWithCancellationAsync(service, CancellationToken.None);

    private static async Task<IResult> HandleWithCancellationAsync(ISubscriptionService service, CancellationToken cancellationToken)
    {
        var plans = await service.GetPlansAsync(cancellationToken);
        return Results.Ok(new SubscriptionPlansResponse(plans.Select(SubscriptionPlanDto.From).ToArray()));
    }
}
