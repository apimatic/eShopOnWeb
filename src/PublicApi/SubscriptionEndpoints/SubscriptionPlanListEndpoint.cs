using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans available in the configured Maxio product family.
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult, GetSubscriptionPlansRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionBillingService billing, CancellationToken ct) =>
            {
                return await HandleAsync(new GetSubscriptionPlansRequest(), billing, ct);
            })
            .Produces<GetSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(GetSubscriptionPlansRequest request, ISubscriptionBillingService billing)
        => HandleAsync(request, billing, CancellationToken.None);

    public async Task<IResult> HandleAsync(GetSubscriptionPlansRequest request, ISubscriptionBillingService billing, CancellationToken ct)
    {
        var response = new GetSubscriptionPlansResponse(request.CorrelationId());
        var plans = await billing.GetPlansAsync(ct);
        response.Plans = plans.Select(p => p.ToDto()).ToList();
        return Results.Ok(response);
    }
}
