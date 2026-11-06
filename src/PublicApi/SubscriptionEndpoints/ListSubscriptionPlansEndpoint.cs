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
/// Lists the subscription plans available to subscribe to
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, ListSubscriptionPlansRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionService subscriptionService, CancellationToken ct) =>
            {
                return await HandleAsync(new ListSubscriptionPlansRequest(), subscriptionService, ct);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ListSubscriptionPlansRequest request, ISubscriptionService subscriptionService)
        => HandleAsync(request, subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(ListSubscriptionPlansRequest request, ISubscriptionService subscriptionService, CancellationToken ct)
    {
        var plans = await subscriptionService.GetPlansAsync(ct);
        var response = new ListSubscriptionPlansResponse(request.CorrelationId())
        {
            Plans = plans.Select(SubscriptionPlanDto.From).ToList()
        };
        return Results.Ok(response);
    }
}
