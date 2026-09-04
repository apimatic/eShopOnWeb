using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class CreateSubscriptionEndpoint : IEndpoint<IResult, SubscribeRequest, SubscriptionService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", async (SubscribeRequest request, SubscriptionService service, CancellationToken cancellationToken) =>
                await HandleAsync(request, service, cancellationToken))
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<SubscribeResponse>(201)
            .Produces(400)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(SubscribeRequest request, SubscriptionService service)
    {
        return HandleAsync(request, service, CancellationToken.None);
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, SubscriptionService service, CancellationToken cancellationToken)
    {
        var result = await service.SubscribeAsync(
            _httpContextAccessor.HttpContext?.User ?? new System.Security.Claims.ClaimsPrincipal(),
            request.PlanHandle,
            cancellationToken);
        var response = new SubscribeResponse
        {
            Subscription = result.Subscription
        };
        return result.Created
            ? Results.Created("api/my-subscriptions", response)
            : Results.Ok(response);
    }
}
