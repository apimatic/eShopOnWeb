using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionPlansEndpoint : IEndpoint<IResult, SubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", async (SubscriptionService service, CancellationToken cancellationToken) =>
                await HandleAsync(service, cancellationToken))
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<SubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(SubscriptionService service)
    {
        return HandleAsync(service, CancellationToken.None);
    }

    public async Task<IResult> HandleAsync(SubscriptionService service, CancellationToken cancellationToken)
    {
        var plans = await service.GetPlansAsync(cancellationToken);
        var response = new SubscriptionPlansResponse();
        response.Plans.AddRange(plans);
        return Results.Ok(response);
    }
}
