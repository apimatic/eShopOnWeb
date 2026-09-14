using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

/// <summary>
/// Lists the subscription plans offered by the configured Maxio product family
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult, CancellationToken>
{
    private readonly ISubscriptionService _subscriptionService;

    public SubscriptionPlanListEndpoint(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CancellationToken ct) =>
            {
                return await HandleAsync(ct);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionPlanEndpoints");
    }

    public async Task<IResult> HandleAsync(CancellationToken ct)
    {
        var response = new ListSubscriptionPlansResponse();

        var plans = await _subscriptionService.ListPlansAsync(ct);

        foreach (var plan in plans)
        {
            response.Plans.Add(SubscriptionPlanDto.From(plan));
        }

        return Results.Ok(response);
    }
}
