using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlansEndpoint : IEndpoint<IResult, SubscriptionService>
{
    private readonly SubscriptionService _service;

    public SubscriptionPlansEndpoint(SubscriptionService service) => _service = service;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (CancellationToken cancellationToken) =>
                await HandleAsync(cancellationToken))
            .Produces<SubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscriptionService service)
    {
        var response = new SubscriptionPlansResponse();
        response.Plans.AddRange(await service.ListPlansAsync(CancellationToken.None));
        return Results.Ok(response);
    }

    private async Task<IResult> HandleAsync(CancellationToken cancellationToken)
    {
        var response = new SubscriptionPlansResponse();
        response.Plans.AddRange(await _service.ListPlansAsync(cancellationToken));
        return Results.Ok(response);
    }
}
