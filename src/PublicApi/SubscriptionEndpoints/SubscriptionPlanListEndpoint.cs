using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans on offer
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionService service, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(service, cancellationToken);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionService service, CancellationToken cancellationToken)
    {
        var response = new ListSubscriptionPlansResponse(new ListSubscriptionPlansRequest().CorrelationId());
        var result = await service.GetPlansAsync(cancellationToken);
        response.Plans = result.Plans;
        response.Truncated = result.Truncated;
        return Results.Ok(response);
    }
}
