using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Constants;
using MinimalApi.Endpoint;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionPlansEndpoint : IEndpoint<IResult, SubscriptionService>
{
    private readonly SubscriptionService _service;

    public SubscriptionPlansEndpoint(SubscriptionService service) => _service = service;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", HandleRoute)
            .RequireAuthorization(AuthorizationConstants.PUBLIC_API_JWT_POLICY)
            .Produces<SubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    private async Task<IResult> HandleRoute(CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(new SubscriptionPlansResponse { Plans = await _service.GetPlansAsync(cancellationToken) });
        }
        catch (MaxioApiException ex)
        {
            return Results.Problem(ex.Message, statusCode: ex.StatusCode == 503 ? 503 : 502);
        }
    }

    public Task<IResult> HandleAsync(SubscriptionService service) =>
        HandleRoute(CancellationToken.None);
}
