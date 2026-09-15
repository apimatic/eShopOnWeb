using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlanListEndpoint : IEndpoint<IResult>
{
    private readonly SubscriptionService _subscriptionService;

    public SubscriptionPlanListEndpoint(SubscriptionService subscriptionService) => _subscriptionService = subscriptionService;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (CancellationToken cancellationToken) =>
            await HandleAsync(cancellationToken))
            .Produces<SubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync() => HandleAsync(CancellationToken.None);

    private async Task<IResult> HandleAsync(CancellationToken cancellationToken)
    {
        var response = new SubscriptionPlansResponse();
        response.Plans.AddRange(await _subscriptionService.ListPlansAsync(cancellationToken));
        return Results.Ok(response);
    }
}
