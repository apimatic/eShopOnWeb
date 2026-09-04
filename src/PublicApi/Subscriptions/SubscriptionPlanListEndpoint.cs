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
    private readonly ISubscriptionService _service;

    public SubscriptionPlanListEndpoint(ISubscriptionService service)
    {
        _service = service;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            (CancellationToken cancellationToken) => HandleAsync(cancellationToken))
            .Produces<SubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        return Results.Ok(await _service.GetPlansAsync(CancellationToken.None));
    }

    private async Task<IResult> HandleAsync(CancellationToken cancellationToken)
    {
        return Results.Ok(await _service.GetPlansAsync(cancellationToken));
    }
}
