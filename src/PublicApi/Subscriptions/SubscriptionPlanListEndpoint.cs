using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlanListEndpoint : IEndpoint<IResult>
{
    private readonly ISubscriptionService _subscriptions;

    public SubscriptionPlanListEndpoint(ISubscriptionService subscriptions)
    {
        _subscriptions = subscriptions;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", async (CancellationToken cancellationToken) =>
            Results.Ok(new ListSubscriptionPlansResponse
            {
                Plans = new(await _subscriptions.ListPlansAsync(cancellationToken))
            }))
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync() =>
        Task.FromResult<IResult>(Results.StatusCode(StatusCodes.Status405MethodNotAllowed));
}
