using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscribable plans in the configured Maxio product family.
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult, ISubscriptionAppService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionAppService subscriptionAppService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(subscriptionAppService, cancellationToken);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionAppService subscriptionAppService)
        => await HandleAsync(subscriptionAppService, default);

    private async Task<IResult> HandleAsync(ISubscriptionAppService subscriptionAppService, CancellationToken cancellationToken)
    {
        var response = new ListSubscriptionPlansResponse();
        response.Plans.AddRange(await subscriptionAppService.GetPlansAsync(cancellationToken));
        return Results.Ok(response);
    }
}
