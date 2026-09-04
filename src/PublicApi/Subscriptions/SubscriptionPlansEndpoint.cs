using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlansEndpoint : IEndpoint<IResult, MaxioSubscriptionService>
{
    private readonly MaxioSubscriptionService _service;

    public SubscriptionPlansEndpoint(MaxioSubscriptionService service)
    {
        _service = service;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", async (CancellationToken cancellationToken) =>
            await HandleRouteAsync(_service, cancellationToken))
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme
            })
            .Produces<SubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(MaxioSubscriptionService service)
    {
        return HandleRouteAsync(service, CancellationToken.None);
    }

    private async Task<IResult> HandleRouteAsync(MaxioSubscriptionService service, CancellationToken cancellationToken)
    {
        var plans = await service.GetPlansAsync(cancellationToken);
        return Results.Ok(new SubscriptionPlansResponse { Plans = plans });
    }
}
